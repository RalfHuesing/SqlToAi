using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// M2.4 acceptance: script execution resolves the qualified target once at entry, carries one
/// immutable target context through every preflight batch, connection and batch execution, and
/// keeps same-raw-name owners isolated under alternating and parallel requests. Uses the real
/// guard pipeline (<see cref="QuerySafetyValidator"/> + <see cref="SecurityGuard"/> + grant-derived
/// access levels), the real <see cref="QueryExecutionService"/> batch executor and the real
/// <see cref="TokenVault"/>/<see cref="Anonymizer"/>/<see cref="QueryTokenResolver"/> chain over
/// target-aware database doubles — not mocked booleans alone.
/// </summary>
public sealed class ScriptExecutionTargetIsolationTests
{
    private const string TestTarget = TestConstants.QualifiedDatabaseName;
    private const string ProdTarget = TestConstants.AlternateQualifiedDatabaseName;

    // -------------------------------------------------------------------------
    // Entry resolution: invalid targets never touch SQL work, including the
    // empty-script edge where resolution must still happen first.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("DemoDB", "SELECT 1")]            // unqualified raw name
    [InlineData("nosuch/DemoDB", "SELECT 1")]     // unknown alias
    [InlineData("/DemoDB", "SELECT 1")]           // empty alias
    [InlineData("te st/DemoDB", "SELECT 1")]      // malformed alias
    [InlineData("", "SELECT 1")]                  // empty string
    [InlineData("DemoDB", "")]                    // invalid target with empty script
    [InlineData("nosuch/DemoDB", "  \r\n ")]      // unknown alias with whitespace-only script
    public async Task ExecuteAsync_RejectsInvalidTargets_BeforeAnySqlWork(string target, string script)
    {
        var (options, factory) = BuildIsolationEnvironment();
        var service = BuildScriptService(options, factory, out var executor);

        var report = await service.ExecuteAsync(
            BuildRequest(script, target), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Failed, report.Status);
        Assert.Equal(SqlToAiError.InvalidParametersCode, report.Error!.Code);
        Assert.Equal(ScriptTransactionMode.NotStarted, report.Mode);
        Assert.Empty(factory.CreatedTargets);
        Assert.Empty(executor.RecordedTargets);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsValidButDeniedTarget_BeforeSqlWork()
    {
        var (options, factory) = BuildIsolationEnvironment();
        var service = BuildScriptService(options, factory, out var executor);

        // prod/SecretDb is a validly formed target on a configured alias, but no grant lists it.
        var report = await service.ExecuteAsync(
            BuildRequest("SELECT Secret FROM T", "prod/SecretDb"), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Failed, report.Status);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, report.Error!.Code);
        Assert.Equal(ScriptTransactionMode.NotStarted, report.Mode);
        Assert.Empty(factory.CreatedTargets);
        Assert.Empty(executor.RecordedTargets);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsServerExcludedTarget_BeforeSqlWork()
    {
        var (options, factory) = BuildIsolationEnvironment();
        options.Servers[TestTargets.AlternateServerAlias].ExcludedDatabases.Add("Temp*");
        var service = BuildScriptService(options, factory, out var executor);

        var report = await service.ExecuteAsync(
            BuildRequest("SELECT Secret FROM T", "prod/TempReporting"), TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Failed, report.Status);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, report.Error!.Code);
        Assert.Empty(factory.CreatedTargets);
        Assert.Empty(executor.RecordedTargets);
    }

    // -------------------------------------------------------------------------
    // Canonical alias: case-variant input resolves to one canonical label in the
    // report while the raw physical database reaches the factory.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CanonicalizesAliasCase_InReportAndReachesRawPhysicalDatabase()
    {
        var (options, factory) = BuildIsolationEnvironment();
        var service = BuildScriptService(options, factory, out var executor);

        var report = await service.ExecuteAsync(
            BuildRequest("SELECT Secret FROM T", "TEST/DemoDB"), TestContext.Current.CancellationToken);

        Assert.True(report.Status == ScriptExecutionStatus.Success, report.Error?.Message);
        Assert.Equal(TestTarget, report.DatabaseName);
        var target = Assert.Single(factory.CreatedTargets);
        Assert.Equal(TestTarget, target.QualifiedName);
        Assert.Equal(TestConstants.DatabaseName, target.DatabaseName);
        Assert.All(executor.RecordedTargets, name => Assert.Equal(TestTarget, name));
    }

    // -------------------------------------------------------------------------
    // Alternating same-raw-name owners with >=2 GO batches and repeat counts:
    // every batch of a script uses one identical target; rights, rows and rules
    // stay with the owner.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AlternatingScriptsWithRepeatCounts_KeepTargetRightsRowsAndRulesSeparate()
    {
        var (options, factory) = BuildIsolationEnvironment();
        var ruleProvider = new TargetRuleProvider();
        var service = BuildScriptService(options, factory, out var executor, ruleProvider);

        string script = "SELECT ExemptValue, Secret FROM T\nGO\nSELECT Secret FROM T\nGO 2\nSELECT Secret FROM T";
        var masked = await service.ExecuteAsync(
            BuildRequest(script, TestTarget), TestContext.Current.CancellationToken);
        var raw = await service.ExecuteAsync(
            BuildRequest(script, ProdTarget), TestContext.Current.CancellationToken);
        var maskedAgain = await service.ExecuteAsync(
            BuildRequest(script, TestTarget), TestContext.Current.CancellationToken);

        Assert.True(masked.Status == ScriptExecutionStatus.Success, masked.Error?.Message);
        Assert.True(raw.Status == ScriptExecutionStatus.Success, raw.Error?.Message);
        Assert.True(maskedAgain.Status == ScriptExecutionStatus.Success, maskedAgain.Error?.Message);

        // 3 batches, the middle one with repeat count 2 -> 4 executions per script.
        Assert.Equal(3, masked.Batches.Count);
        Assert.Equal(2, masked.Batches[1].Batch.RepeatCount);
        Assert.Equal(2, masked.Batches[1].Executions.Count);
        Assert.Equal(ScriptTransactionMode.ReadOnlyAnonymizedRollback, masked.Mode);
        Assert.Equal(ScriptTransactionMode.ReadOnlyRollback, raw.Mode);

        // Every batch of every script ran with exactly one target context: the masked scripts
        // exclusively with the test owner, the raw script exclusively with the prod owner.
        Assert.Equal(12, executor.RecordedTargets.Count);
        Assert.Equal(8, executor.RecordedTargets.Count(name => name == TestTarget));
        Assert.Equal(4, executor.RecordedTargets.Count(name => name == ProdTarget));

        // Masked owner: exempt column stays raw, secret tokenizes; raw owner keeps plaintext.
        string exempt = ExtractColumnValue(masked.Batches[0].Executions[0].Data, "ExemptValue");
        string secret = ExtractColumnValue(masked.Batches[0].Executions[0].Data, "Secret");
        Assert.Equal("exempt-test-value", exempt);
        Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, secret, StringComparison.Ordinal);
        Assert.Contains("prod-row", raw.Batches[0].Executions[0].Data, StringComparison.Ordinal);
        Assert.DoesNotContain("test-row", raw.Batches[0].Executions[0].Data, StringComparison.Ordinal);
        Assert.DoesNotContain("prod-row", masked.Batches[0].Executions[0].Data, StringComparison.Ordinal);

        // The rule provider was consulted only for the anonymized owner, with its own identity.
        Assert.All(ruleProvider.Owners, owner => Assert.Equal(TestTarget, owner));
    }

    // -------------------------------------------------------------------------
    // Parallel same-raw-name scripts: genuine cross-owner overlap at the
    // connection-open boundary, per-owner rows and one target per script batch.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParallelSameRawNameScripts_DoNotLeakTargetOrState()
    {
        var (options, factory) = BuildIsolationEnvironment();
        var barrier = new OwnerOverlapBarrier();
        factory.OpenBarrier = barrier;
        var service = BuildScriptService(options, factory, out var executor);

        // Two GO batches with a repeat count; each script opens its own connection.
        string script = "SELECT Secret FROM T\nGO\nSELECT Secret FROM T\nGO 2\nSELECT Secret FROM T";
        var runs = Enumerable.Range(0, 12).Select(i => Task.Run(async () =>
        {
            bool useTest = i % 2 == 0;
            var report = await service.ExecuteAsync(
                BuildRequest(script, useTest ? TestTarget : ProdTarget),
                TestContext.Current.CancellationToken);
            return (useTest, report);
        })).ToList();

        await Task.WhenAll(runs);
        Assert.True(barrier.CrossOwnerOverlapReached, "the two owners were never in flight together");

        foreach ((bool useTest, ScriptExecutionReport report) in runs.Select(run => run.Result))
        {
            Assert.True(report.Status == ScriptExecutionStatus.Success, report.Error?.Message);
            Assert.Equal(3, report.Batches.Count);
            Assert.Equal(2, report.Batches[1].Executions.Count);
            string data = report.Batches[0].Executions[0].Data;
            if (useTest)
            {
                Assert.DoesNotContain("test-row", data, StringComparison.Ordinal);
                Assert.DoesNotContain("prod-row", data, StringComparison.Ordinal);
                Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, ExtractColumnValue(data, "Secret"), StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("prod-row", data, StringComparison.Ordinal);
                Assert.DoesNotContain("test-row", data, StringComparison.Ordinal);
            }
        }

        // Every batch execution recorded exactly one target identity per script run: 12 scripts
        // x 4 executions, each owner's executions all sharing that owner's qualified name.
        Assert.Equal(48, executor.RecordedTargets.Count);
        Assert.Equal(24, executor.RecordedTargets.Count(name => name == TestTarget));
        Assert.Equal(24, executor.RecordedTargets.Count(name => name == ProdTarget));
        Assert.Equal(12, factory.CreatedTargets.Count);
        Assert.Equal(6, factory.CreatedTargets.Count(target => target.QualifiedName == TestTarget));
        Assert.Equal(6, factory.CreatedTargets.Count(target => target.QualifiedName == ProdTarget));
    }

    // -------------------------------------------------------------------------
    // Token scoping: script egress mints owner-scoped tokens; scripts themselves
    // keep token text literal (no resolution ingress for scripts); the existing
    // query ingress resolves same-scope tokens only.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ScriptEgressMintsScopedToken_QueryResolvesSameScope_ScriptStaysLiteral()
    {
        var (options, factory) = BuildIsolationEnvironment();
        var sharedVault = new TokenVault();
        var service = BuildScriptService(options, factory, out var executor, vault: sharedVault);
        var queryService = BuildQueryService(options, factory, vault: sharedVault);

        // 1. Egress through a script on the test owner mints a scoped token.
        var egress = await service.ExecuteAsync(
            BuildRequest("SELECT Secret FROM T", TestTarget), TestContext.Current.CancellationToken);
        Assert.True(egress.Status == ScriptExecutionStatus.Success, egress.Error?.Message);
        string token = ExtractColumnValue(egress.Batches[0].Executions[0].Data, "Secret");
        Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, token, StringComparison.Ordinal);

        // 2. A script containing the token text keeps it as literal SQL text — scripts do not
        //    resolve tokens.
        var literal = await service.ExecuteAsync(
            BuildRequest($"SELECT Secret FROM T WHERE Secret = '{token}'", TestTarget),
            TestContext.Current.CancellationToken);
        Assert.True(literal.Status == ScriptExecutionStatus.Success, literal.Error?.Message);
        Assert.Contains($"WHERE Secret = '{token}'", factory.LastConnection?.LastCommand?.CommandText, StringComparison.Ordinal);

        // 3. The existing supported query ingress on the same target resolves the token back to
        //    the real value.
        await queryService.ExecuteQueryAsync(
            TestTarget, $"SELECT * FROM T WHERE Secret = '{token}'", null, TestContext.Current.CancellationToken);
        Assert.Equal(
            "SELECT * FROM T WHERE Secret = 'test-row'",
            factory.LastConnection?.LastCommand?.CommandText);

        // 4. The same token inside the prod owner's query scope is foreign and stays literal.
        await queryService.ExecuteQueryAsync(
            ProdTarget, $"SELECT * FROM T WHERE Secret = '{token}'", null, TestContext.Current.CancellationToken);
        Assert.Equal(
            $"SELECT * FROM T WHERE Secret = '{token}'",
            factory.LastConnection?.LastCommand?.CommandText);
    }

    // -------------------------------------------------------------------------
    // Helpers.
    // -------------------------------------------------------------------------

    private static (SqlToAiOptions Options, TargetRowsConnectionFactory Factory) BuildIsolationEnvironment()
    {
        var options = AnonymizationTestHelper.BuildTokenizationOptions();
        options.WithNamedServers(TestTargets.ServerAlias, TestTargets.AlternateServerAlias);
        options.Databases.ReadOnlyAnonymized.Add(TestTarget);
        options.Databases.ReadOnly.Add(ProdTarget);
        var rows = new Dictionary<string, TargetRowSet>(StringComparer.Ordinal)
        {
            [TestTarget] = new(
                ["ExemptValue", "Secret"],
                [["exempt-test-value", "test-row"]]),
            [ProdTarget] = new(
                ["ExemptValue", "Secret"],
                [["exempt-prod-value", "prod-row"]]),
        };
        return (options, new TargetRowsConnectionFactory(rows));
    }

    private static ScriptExecutionService BuildScriptService(
        SqlToAiOptions options,
        TargetRowsConnectionFactory factory,
        out RecordingDelegatingBatchExecutor executor,
        TargetRuleProvider? ruleProvider = null,
        TokenVault? vault = null)
    {
        executor = new RecordingDelegatingBatchExecutor(BuildQueryService(options, factory, ruleProvider, vault));
        return new ScriptExecutionService(
            factory,
            BuildSafetyValidator(options),
            executor,
            Options.Create(options),
            NullLogger<ScriptExecutionService>.Instance);
    }

    private static QueryExecutionService BuildQueryService(
        SqlToAiOptions options,
        TargetRowsConnectionFactory factory,
        TargetRuleProvider? ruleProvider = null,
        TokenVault? vault = null)
    {
        vault ??= new TokenVault();
        var anonymizer = new Anonymizer(Options.Create(options), vault);
        var resolver = new QueryTokenResolver(vault, Options.Create(options));
        return new QueryExecutionService(
            factory,
            BuildSafetyValidator(options),
            new AnonymizationDependencies(anonymizer, ruleProvider ?? new TargetRuleProvider(), resolver),
            Options.Create(options),
            NullLogger<QueryExecutionService>.Instance);
    }

    private static QuerySafetyValidator BuildSafetyValidator(SqlToAiOptions options) =>
        new(
            new SecurityGuard(Options.Create(options)),
            new GrantsAccessLevelProvider(options),
            new ReadOnlyGuard());

    private static ScriptExecutionRequest BuildRequest(string text, string target) =>
        new(new SqlScriptFile("script.sql", text, "UTF-8"), target);

    /// <summary>Wraps the real <see cref="QueryExecutionService"/> batch executor and records the
    /// target identity of every executed batch so per-batch target fidelity is observable.</summary>
    private sealed class RecordingDelegatingBatchExecutor(IQueryBatchExecutor inner) : IQueryBatchExecutor
    {
        private readonly object _gate = new();

        public List<string> RecordedTargets { get; } = [];

        public async Task<Result<QueryExecutionResult>> ExecuteBatchAsync(
            QueryBatchExecutionArgs args,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                RecordedTargets.Add(args.Target.QualifiedName);
            }

            return await inner.ExecuteBatchAsync(args, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ExtractColumnValue(string jsonLinesData, string columnName)
    {
        string firstLine = jsonLinesData.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        using var doc = JsonDocument.Parse(firstLine);
        return doc.RootElement.GetProperty(columnName).GetString()!;
    }
}
