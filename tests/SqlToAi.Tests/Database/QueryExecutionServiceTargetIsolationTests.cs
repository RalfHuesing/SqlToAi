using System.Data.Common;
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
/// M2.3 acceptance: alternate and concurrent same-raw-name targets through query execution and
/// validation use the right endpoint, access level, masking rules and token scope. Uses the real
/// guard pipeline (real <see cref="QuerySafetyValidator"/> + real <see cref="SecurityGuard"/>),
/// a real <see cref="TokenVault"/>/<see cref="Anonymizer"/>/<see cref="QueryTokenResolver"/> chain
/// and target-aware database doubles with distinct rows per owner — not mocked booleans alone.
/// </summary>
public sealed class QueryExecutionServiceTargetIsolationTests
{
    private const string TestTarget = TestConstants.QualifiedDatabaseName;
    private const string ProdTarget = TestConstants.AlternateQualifiedDatabaseName;

    // -------------------------------------------------------------------------
    // Direct-service target resolution: invalid inputs never touch a connection.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("DemoDB")]           // unqualified raw name
    [InlineData("nosuch/DemoDB")]    // unknown alias
    [InlineData("/DemoDB")]          // empty alias
    [InlineData("te st/DemoDB")]     // malformed alias
    [InlineData("")]                 // empty
    public async Task ExecuteQueryAsync_RejectsInvalidTargets_BeforeAnySqlWork(string target)
    {
        var (options, factory) = BuildIsolationEnvironment();
        var service = BuildExecutionService(options, factory);

        var result = await service.ExecuteQueryAsync(target, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Empty(factory.CreatedTargets);
    }

    [Theory]
    [InlineData("DemoDB")]
    [InlineData("nosuch/DemoDB")]
    public async Task ValidateQueryAsync_RejectsInvalidTargets_BeforeAnySqlWork(string target)
    {
        var (options, factory) = BuildIsolationEnvironment();
        var service = new QueryValidationService(
            factory,
            BuildSafetyValidator(options),
            Options.Create(options),
            NullLogger<QueryValidationService>.Instance);

        var result = await service.ValidateQueryAsync(target, "SELECT 1", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Empty(factory.CreatedTargets);
    }

    [Fact]
    public async Task ExecuteQueryAsync_RejectsValidButDeniedTarget_BeforeSqlWork()
    {
        var (options, factory) = BuildIsolationEnvironment();
        var service = BuildExecutionService(options, factory);

        // prod/SecretDb is a validly formed target on a configured alias, but no grant lists it.
        var result = await service.ExecuteQueryAsync("prod/SecretDb", "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Empty(factory.CreatedTargets);
    }

    [Fact]
    public async Task ExecuteQueryAsync_RejectsServerExcludedTarget_BeforeSqlWork()
    {
        var (options, factory) = BuildIsolationEnvironment();
        options.Servers["prod"].ExcludedDatabases.Add("Temp*");
        var service = BuildExecutionService(options, factory);

        var result = await service.ExecuteQueryAsync("prod/TempReporting", "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Empty(factory.CreatedTargets);
    }

    // -------------------------------------------------------------------------
    // Canonical alias resolution: case-variant alias input resolves to one owner.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_CanonicalizesAliasCase_AndReachesRawPhysicalDatabase()
    {
        var (options, factory) = BuildIsolationEnvironment();
        var service = BuildExecutionService(options, factory);

        var result = await service.ExecuteQueryAsync("TEST/DemoDB", "SELECT Secret FROM T", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        var target = Assert.Single(factory.CreatedTargets);
        Assert.Equal(TestTarget, target.QualifiedName);
        Assert.Equal(TestConstants.DatabaseName, target.DatabaseName);
        // The canonical owner is ReadOnlyAnonymized, so the value is tokenized, not raw.
        Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, ExtractColumnValue(result.Value.Data, "Secret"), StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Alternating same-raw-name owners: distinct rights, rows and rules per target.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AlternatingSameRawNameTargets_KeepRightsRowsAndRulesSeparate()
    {
        var (options, factory) = BuildIsolationEnvironment();
        var ruleProvider = new TargetRuleProvider();
        var service = BuildExecutionService(options, factory, ruleProvider);

        // First call: test owner is ReadOnlyAnonymized and masks; prod owner is ReadOnly raw.
        var masked = await service.ExecuteQueryAsync(TestTarget, "SELECT Secret FROM T", null, TestContext.Current.CancellationToken);
        var raw = await service.ExecuteQueryAsync(ProdTarget, "SELECT Secret FROM T", null, TestContext.Current.CancellationToken);
        var maskedAgain = await service.ExecuteQueryAsync(TestTarget, "SELECT Secret FROM T", null, TestContext.Current.CancellationToken);

        Assert.True(masked.IsSuccess);
        Assert.True(raw.IsSuccess);
        Assert.True(maskedAgain.IsSuccess);
        Assert.DoesNotContain("test-row", masked.Value.Data, StringComparison.Ordinal);
        Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, ExtractColumnValue(masked.Value.Data, "Secret"), StringComparison.Ordinal);
        Assert.Contains("prod-row", raw.Value.Data, StringComparison.Ordinal);
        // The repeated masked call shows the same owner's masking again, never the other owner's row.
        Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, ExtractColumnValue(maskedAgain.Value.Data, "Secret"), StringComparison.Ordinal);
        Assert.DoesNotContain("prod-row", maskedAgain.Value.Data, StringComparison.Ordinal);
        Assert.True(masked.Value.WasAnonymized);
        Assert.False(raw.Value.WasAnonymized);
        Assert.True(maskedAgain.Value.WasAnonymized);

        // The rule provider is consulted only for the anonymized owner, and always with its
        // own target identity — never a bare database name or another owner's context.
        Assert.Equal([TestTarget, TestTarget], ruleProvider.Owners);
    }

    [Fact]
    public async Task ParallelSameRawNameTargets_DoNotLeakState()
    {
        var (options, factory) = BuildIsolationEnvironment();
        var service = BuildExecutionService(options, factory);

        var calls = Enumerable.Range(0, 24).Select(async i =>
        {
            bool useTest = i % 2 == 0;
            var result = await service.ExecuteQueryAsync(
                useTest ? TestTarget : ProdTarget, "SELECT Secret FROM T", null, TestContext.Current.CancellationToken);
            return (useTest, result);
        }).ToList();

        await Task.WhenAll(calls);
        foreach ((bool useTest, Result<QueryExecutionResult> result) in calls.Select(call => call.Result))
        {
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
            if (useTest)
            {
                Assert.DoesNotContain("test-row", result.Value.Data, StringComparison.Ordinal);
                Assert.True(result.Value.WasAnonymized);
            }
            else
            {
                Assert.Contains("prod-row", result.Value.Data, StringComparison.Ordinal);
                Assert.False(result.Value.WasAnonymized);
            }
        }
        Assert.Equal(24, factory.CreatedTargets.Count);
        Assert.Equal(12, factory.CreatedTargets.Count(target => target.QualifiedName == TestTarget));
    }

    // -------------------------------------------------------------------------
    // Token scoping through the real vault: own tokens substitute, foreign stay literal.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OwnTokenSubstitutes_ForeignTokenStaysLiteral_InSupportedQueryLiteralPath()
    {
        var (options, factory) = BuildIsolationEnvironment();
        var service = BuildExecutionService(options, factory);

        // 1. Egress on the test owner mints a scoped token for the anonymized value.
        var egress = await service.ExecuteQueryAsync(TestTarget, "SELECT Secret FROM T", null, TestContext.Current.CancellationToken);
        Assert.True(egress.IsSuccess);
        string token = ExtractColumnValue(egress.Value.Data, "Secret");
        Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, token, StringComparison.Ordinal);

        // 2. Ingress on the same owner substitutes the token back to the real value.
        await service.ExecuteQueryAsync(TestTarget, $"SELECT * FROM T WHERE Secret = '{token}'", null, TestContext.Current.CancellationToken);
        Assert.Equal(
            "SELECT * FROM T WHERE Secret = 'test-row'",
            factory.LastConnection?.LastCommand?.CommandText);

        // 3. The same token in the prod owner's scope is foreign and stays literal SQL text.
        await service.ExecuteQueryAsync(ProdTarget, $"SELECT * FROM T WHERE Secret = '{token}'", null, TestContext.Current.CancellationToken);
        Assert.Equal(
            $"SELECT * FROM T WHERE Secret = '{token}'",
            factory.LastConnection?.LastCommand?.CommandText);
    }

    [Fact]
    public async Task RuleExcludedColumnStaysRaw_WhileOtherColumnsAreTokenized()
    {
        var (options, factory) = BuildIsolationEnvironment();
        var ruleProvider = new TargetRuleProvider();
        var service = BuildExecutionService(options, factory, ruleProvider);

        var result = await service.ExecuteQueryAsync(TestTarget, "SELECT ExemptValue, Secret FROM T", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        string exempt = ExtractColumnValue(result.Value.Data, "ExemptValue");
        string secret = ExtractColumnValue(result.Value.Data, "Secret");
        Assert.Equal("exempt-test-value", exempt);
        Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, secret, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Raw unusual and case-sensitive database names survive scope boundaries.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RawDatabaseNameWithSlash_ReachesFactoryExactly_AndScopesTokensSeparately()
    {
        var (options, factory) = BuildIsolationEnvironment(withProdRows: false);
        options.Databases.ReadOnlyAnonymized.Add("test/App/Db");
        var service = BuildExecutionService(options, factory);

        var result = await service.ExecuteQueryAsync("test/App/Db", "SELECT Secret FROM T", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        var target = Assert.Single(factory.CreatedTargets);
        Assert.Equal("App/Db", target.DatabaseName);
        string token = ExtractColumnValue(result.Value.Data, "Secret");

        // The identical plaintext in another owner's raw database scope must not resolve there.
        options.Databases.ReadOnlyAnonymized.Add("test/appdb");
        var second = await service.ExecuteQueryAsync("test/appdb", $"SELECT * FROM T WHERE Secret = '{token}'", null, TestContext.Current.CancellationToken);
        Assert.True(second.IsSuccess);
        Assert.Equal(
            $"SELECT * FROM T WHERE Secret = '{token}'",
            factory.LastConnection?.LastCommand?.CommandText);
    }

    // -------------------------------------------------------------------------
    // Helpers.
    // -------------------------------------------------------------------------

    private static (SqlToAiOptions Options, TargetRowsConnectionFactory Factory) BuildIsolationEnvironment(bool withProdRows = true)
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
        };
        if (withProdRows)
        {
            rows[ProdTarget] = new(
                ["ExemptValue", "Secret"],
                [["exempt-prod-value", "prod-row"]]);
        }
        // Ordinally distinct raw database names sharing one alias get their own physical rows.
        rows["test/App/Db"] = rows[TestTarget];
        rows["test/appdb"] = new(["ExemptValue", "Secret"], [["exempt-test-value", "appdb-row"]]);
        return (options, new TargetRowsConnectionFactory(rows));
    }

    private static QueryExecutionService BuildExecutionService(
        SqlToAiOptions options,
        TargetRowsConnectionFactory factory,
        TargetRuleProvider? ruleProvider = null)
    {
        var vault = new TokenVault();
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
            new TargetAccessLevelProvider(new Dictionary<string, AccessLevel>(StringComparer.Ordinal)
            {
                [TestTarget] = AccessLevel.ReadOnlyAnonymized,
                [ProdTarget] = AccessLevel.ReadOnly,
                ["test/App/Db"] = AccessLevel.ReadOnlyAnonymized,
                ["test/appdb"] = AccessLevel.ReadOnlyAnonymized,
            }),
            new ReadOnlyGuard());

    private static string ExtractColumnValue(string jsonLinesData, string columnName)
    {
        string firstLine = jsonLinesData.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        using var doc = System.Text.Json.JsonDocument.Parse(firstLine);
        return doc.RootElement.GetProperty(columnName).GetString()!;
    }

    /// <summary>One physical row set per qualified owner: two columns, where the central rule
    /// provider exempts ExemptValue for the test owner only.</summary>
    private sealed record TargetRowSet(string[] Columns, object?[][] Rows);

    /// <summary>Target-aware database double: one physical row set per qualified owner. The
    /// connection's Database carries only the raw physical database name, like SqlClient.</summary>
    private sealed class TargetRowsConnectionFactory(Dictionary<string, TargetRowSet> rowsByTarget) : IDatabaseConnectionFactory
    {
        public List<DatabaseTargetContext> CreatedTargets { get; } = [];

        public FakeDbConnection? LastConnection { get; private set; }

        public DbConnection CreateTargetConnection(DatabaseTargetContext target)
        {
            CreatedTargets.Add(target);
            TargetRowSet rowSet = rowsByTarget[target.QualifiedName];
            LastConnection = new FakeDbConnection(
                conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
                    ExecuteScalar: _ => 1,
                    ExecuteReader: cmd =>
                    {
                        conn.LastCommand = cmd;
                        return new FakeDbDataReader(
                            rowSet.Columns,
                            rowSet.Rows,
                            // Column 0 carries a real origin (ExemptValue), column 1 has none —
                            // so the exemption decision only ever applies to ExemptValue.
                            new FakeSchemaTableOrigin("T", "ExemptValue", "dbo"));
                    },
                    ExecuteNonQuery: _ => 0)),
                new FakeDbConnectionOptions(
                    Database: target.DatabaseName,
                    DataSource: "mock",
                    ServerVersion: "16.0",
                    BeginTransaction: (transactionConnection, _) => new FakeDbTransaction(transactionConnection)));
            return LastConnection;
        }

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

        public DbConnection CreateConnection(string? databaseName = null) => throw new NotSupportedException();
    }

    /// <summary>Per-target central rule provider: exempts only the test owner's ExemptValue column
    /// and records the owning target identity of every lookup.</summary>
    private sealed class TargetRuleProvider : IAnonymizationRuleProvider
    {
        public List<string> Owners { get; } = [];

        public Task<bool> IsExcludedAsync(DatabaseTargetContext target, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
        {
            Owners.Add(target.QualifiedName);
            return Task.FromResult(
                target.QualifiedName == TestTarget
                && string.Equals(columnName, "ExemptValue", StringComparison.OrdinalIgnoreCase));
        }

        public Task<bool> IsExcludedAsync(string databaseName, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Target-isolated execution must not fall back to the unscoped rule path.");
    }

    /// <summary>Per-target access levels, standing in for the real SQL access-level probe.</summary>
    private sealed class TargetAccessLevelProvider(Dictionary<string, AccessLevel> levels) : IAccessLevelProvider
    {
        public Task<AccessLevel> GetTargetAccessLevelAsync(DatabaseTargetContext target, CancellationToken cancellationToken = default) =>
            Task.FromResult(levels.TryGetValue(target.QualifiedName, out AccessLevel level) ? level : AccessLevel.None);

        public Task<AccessLevel> GetAccessLevelAsync(string databaseName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Target-isolated execution must not fall back to the unscoped access probe.");
    }
}
