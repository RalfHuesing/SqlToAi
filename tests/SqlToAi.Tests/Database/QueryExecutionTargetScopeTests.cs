using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// M2.3 acceptance follow-ups: token scoping when <em>both</em> owners are anonymized (so the
/// scoped resolver is genuinely exercised, not bypassed by a non-anonymized access level), a
/// rule-exemption contrast across owners, ordinally case-distinct raw databases, and composed
/// positive query-validation cases over the real guard pipeline.
/// </summary>
public sealed class QueryExecutionTargetScopeTests
{
    private const string TestTarget = TestConstants.QualifiedDatabaseName;
    private const string ProdTarget = TestConstants.AlternateQualifiedDatabaseName;

    [Fact]
    public async Task IdenticalPlaintext_BothOwnersAnonymized_MintsDistinctTokens_AndIngressResolvesOnlyInOwner()
    {
        var (options, factory) = BuildBothAnonymizedEnvironment();
        var service = BuildExecutionService(options, factory);

        // 1. Identical fictional plaintext on both owners mints two distinct scoped tokens.
        string testToken = ExtractColumnValue(await Execute(service, factory, TestTarget, "SELECT Secret FROM T"), "Secret");
        string prodToken = ExtractColumnValue(await Execute(service, factory, ProdTarget, "SELECT Secret FROM T"), "Secret");
        Assert.NotEqual(testToken, prodToken);

        // 2. Each owner's own token substitutes back to the shared plaintext in its own scope.
        Assert.Equal(
            "SELECT * FROM T WHERE Secret = 'shared-secret'",
            await ExecuteCapturingSql(service, factory, TestTarget, $"SELECT * FROM T WHERE Secret = '{testToken}'"));
        Assert.Equal(
            "SELECT * FROM T WHERE Secret = 'shared-secret'",
            await ExecuteCapturingSql(service, factory, ProdTarget, $"SELECT * FROM T WHERE Secret = '{prodToken}'"));

        // 3. Each token is foreign in the other owner's scope and stays literal SQL text.
        Assert.Equal(
            $"SELECT * FROM T WHERE Secret = '{testToken}'",
            await ExecuteCapturingSql(service, factory, ProdTarget, $"SELECT * FROM T WHERE Secret = '{testToken}'"));
        Assert.Equal(
            $"SELECT * FROM T WHERE Secret = '{prodToken}'",
            await ExecuteCapturingSql(service, factory, TestTarget, $"SELECT * FROM T WHERE Secret = '{prodToken}'"));
    }

    [Fact]
    public async Task ExemptionContrast_BothOwnersAnonymized_ExemptStaysRaw_OnlyForItsOwner()
    {
        var (options, factory) = BuildBothAnonymizedEnvironment();
        var ruleProvider = new TargetRuleProvider();
        var service = BuildExecutionService(options, factory, ruleProvider);

        string testExempt = ExtractColumnValue(await Execute(service, factory, TestTarget, "SELECT ExemptValue, Secret FROM T"), "ExemptValue");
        string prodExempt = ExtractColumnValue(await Execute(service, factory, ProdTarget, "SELECT ExemptValue, Secret FROM T"), "ExemptValue");

        // Same query, same rule provider: the test owner's exemption applies there and nowhere else.
        Assert.Equal("exempt-test-DemoDB", testExempt);
        Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, prodExempt, StringComparison.Ordinal);
        Assert.All(ruleProvider.Owners, owner => Assert.Contains(owner, new[] { TestTarget, ProdTarget }));
        Assert.Contains(ProdTarget, ruleProvider.Owners);
    }

    [Fact]
    public async Task CaseOnlyDistinctRawDatabases_OnOneAlias_DoNotShareTokenScope()
    {
        var (options, factory) = BuildBothAnonymizedEnvironment();
        var service = BuildExecutionService(options, factory);

        string upperToken = ExtractColumnValue(await Execute(service, factory, "test/AppDb", "SELECT Secret FROM T"), "Secret");

        // Identical plaintext minted in the ordinally distinct raw scope is a different token...
        string lowerToken = ExtractColumnValue(await Execute(service, factory, "test/appdb", "SELECT Secret FROM T"), "Secret");
        Assert.NotEqual(upperToken, lowerToken);

        // ...and the upper scope's token does not resolve in the lower scope.
        Assert.Equal(
            $"SELECT * FROM T WHERE Secret = '{upperToken}'",
            await ExecuteCapturingSql(service, factory, "test/appdb", $"SELECT * FROM T WHERE Secret = '{upperToken}'"));
        Assert.Equal(
            "SELECT * FROM T WHERE Secret = 'shared-secret'",
            await ExecuteCapturingSql(service, factory, "test/AppDb", $"SELECT * FROM T WHERE Secret = '{upperToken}'"));
    }

    [Theory]
    [InlineData("SELECT 1 AS [a]]'§§§known§§§']")]
    [InlineData("SELECT 1 AS \"'§§§known§§§'\"")]
    [InlineData("SELECT 1 AS \"a\"\"'§§§known§§§'\"")]
    [InlineData("SELECT 1 AS '§§§known§§§'")]
    [InlineData("SELECT 1 '§§§known§§§'")]
    [InlineData("SELECT '§§§known§§§' = 1")]
    [InlineData("SELECT 1 AS 'a''§§§known§§§'")]
    public async Task QueryExecution_PreservesTokenTextInIdentifierAndOutputKey(string query)
    {
        var options = AnonymizationTestHelper.BuildTokenizationOptions();
        options.WithNamedServers(TestTargets.ServerAlias);
        options.Databases.ReadOnlyAnonymized.Add(TestTarget);

        var target = new DatabaseTargetContext(TestTargets.ServerAlias, TestConstants.DatabaseName);
        var vault = new TokenVault();
        vault.Store("§§§known§§§", "PLAINTEXT", target);
        var resolver = new QueryTokenResolver(vault, Options.Create(options));
        var anonymizer = new Anonymizer(Options.Create(options), vault);
        var factory = new TargetRowsConnectionFactory(
            new Dictionary<string, TargetRowSet>(StringComparer.Ordinal)
            {
                [TestTarget] = new TargetRowSet(["unused"], [[1]])
            },
            ExtractOutputAlias);
        var service = new QueryExecutionService(
            factory,
            new QuerySafetyValidator(
                new SecurityGuard(Options.Create(options)),
                new GrantsAccessLevelProvider(options),
                new ReadOnlyGuard()),
            new AnonymizationDependencies(anonymizer, null, resolver),
            Options.Create(options),
            NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestTarget, query, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        Assert.Equal(query, factory.LastConnection!.LastCommand!.CommandText);
        using var document = System.Text.Json.JsonDocument.Parse(result.Value.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);
        string outputKey = Assert.Single(document.RootElement.EnumerateObject()).Name;
        Assert.Contains("§§§known§§§", outputKey, StringComparison.Ordinal);
        Assert.DoesNotContain("PLAINTEXT", outputKey, StringComparison.Ordinal);
    }

    private static string[] ExtractOutputAlias(string commandText)
    {
        var parsed = SqlScriptDomParser.ParseScript(commandText);
        Assert.True(parsed.Success);
        var statement = Assert.IsType<SelectStatement>(Assert.Single(Assert.Single(parsed.Script!.Batches).Statements));
        var query = Assert.IsType<QuerySpecification>(statement.QueryExpression);
        var column = Assert.IsType<SelectScalarExpression>(Assert.Single(query.SelectElements));
        return [column.ColumnName.Value];
    }

    [Fact]
    public async Task ValidateQueryAsync_ComposedGuard_ResolvesCanonicalTargets_AndRollsBack()
    {
        var (options, factory) = BuildBothAnonymizedEnvironment();
        options.Databases.ReadOnlyAnonymized.Clear();
        options.Databases.ReadWrite.Add(TestTarget);
        options.Databases.ReadWrite.Add(ProdTarget);
        var service = new QueryValidationService(
            factory,
            new QuerySafetyValidator(
                new SecurityGuard(Options.Create(options)),
                new GrantsAccessLevelProvider(options),
                new ReadOnlyGuard()),
            Options.Create(options),
            NullLogger<QueryValidationService>.Instance);

        var testResult = await service.ValidateQueryAsync(TestTarget, "SELECT 1", TestContext.Current.CancellationToken);
        var canonicalizedResult = await service.ValidateQueryAsync("PROD/DemoDB", "SELECT 1", TestContext.Current.CancellationToken);

        Assert.True(testResult.IsSuccess, testResult.IsFailure ? testResult.Error.Message : string.Empty);
        Assert.True(canonicalizedResult.IsSuccess, canonicalizedResult.IsFailure ? canonicalizedResult.Error.Message : string.Empty);
        var targets = factory.CreatedTargets;
        Assert.Equal(2, targets.Count);
        Assert.Equal(TestTarget, targets[0].QualifiedName);
        Assert.Equal(ProdTarget, targets[1].QualifiedName);
        Assert.All(targets, target => Assert.Equal(TestConstants.DatabaseName, target.DatabaseName));
        // Validation never commits; each call rolls back its own selected-owner connection.
        Assert.All(new[] { testResult, canonicalizedResult }, _ =>
        {
            Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
            Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
        });
    }

    private static (SqlToAiOptions Options, TargetRowsConnectionFactory Factory) BuildBothAnonymizedEnvironment()
    {
        var options = AnonymizationTestHelper.BuildTokenizationOptions();
        options.WithNamedServers(TestTargets.ServerAlias, TestTargets.AlternateServerAlias);
        foreach (string target in new[] { TestTarget, ProdTarget, "test/AppDb", "test/appdb" })
        {
            options.Databases.ReadOnlyAnonymized.Add(target);
        }
        var rows = new Dictionary<string, TargetRowSet>(StringComparer.Ordinal);
        foreach (string target in new[] { TestTarget, ProdTarget, "test/AppDb", "test/appdb" })
        {
            // Identical plaintext everywhere, so only the target scope can tell the tokens apart.
            rows[target] = new TargetRowSet(
                ["ExemptValue", "Secret"],
                [[$"exempt-{target.Replace('/', '-')}", "shared-secret"]]);
        }
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
            new QuerySafetyValidator(
                new SecurityGuard(Options.Create(options)),
                new GrantsAccessLevelProvider(options),
                new ReadOnlyGuard()),
            new AnonymizationDependencies(anonymizer, ruleProvider ?? new TargetRuleProvider(), resolver),
            Options.Create(options),
            NullLogger<QueryExecutionService>.Instance);
    }

    private static async Task<string> Execute(
        QueryExecutionService service, TargetRowsConnectionFactory factory, string target, string query)
    {
        var result = await service.ExecuteQueryAsync(target, query, null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value.Data;
    }

    /// <summary>Runs an ingress query and returns the command text the factory's last connection
    /// actually executed — the ground truth for substitution versus literal token text.</summary>
    private static async Task<string> ExecuteCapturingSql(
        QueryExecutionService service, TargetRowsConnectionFactory factory, string target, string query)
    {
        var result = await service.ExecuteQueryAsync(target, query, null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        return factory.LastConnection!.LastCommand!.CommandText;
    }

    private static string ExtractColumnValue(string jsonLinesData, string columnName)
    {
        string firstLine = jsonLinesData.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        using var doc = System.Text.Json.JsonDocument.Parse(firstLine);
        return doc.RootElement.GetProperty(columnName).GetString()!;
    }
}
