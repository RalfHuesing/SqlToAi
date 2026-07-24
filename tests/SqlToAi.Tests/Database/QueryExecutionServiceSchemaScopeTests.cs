#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Schema-scoped exclusion/rule matching regression tests (audit finding — see
/// tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md, Finding "Ausschluss-/Regel-Abgleich
/// ist schema-blind — gleichnamige Tabelle in anderem Schema erbt fremde Freigabe"). Reproduces the
/// exact audit scenario: two same-named "Kunden" tables in different schemas, one exempted from
/// anonymization, the other not. Split into its own file (not a third partial of
/// <see cref="QueryExecutionServiceTests"/> — see <c>MaxPartialClassFiles</c>) reusing the shared
/// mock DB fakes from <c>QueryExecutionServiceMockDb.cs</c>.
/// </summary>
public sealed class QueryExecutionServiceSchemaScopeTests
{
    private static QueryExecutionService BuildSchemaScopedService(
        SqlToAiOptions options, MockQueryConnectionFactory factory, IAnonymizationRuleProvider? ruleProvider = null, IAnonymizerExclusionProvider? exclusionProvider = null) =>
        new(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
            new FakeReadOnlyGuard(true),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault()), exclusionProvider, ruleProvider),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

    [Fact]
    public async Task ExecuteQueryAsync_ShouldExcludeOnlyMatchingSchema_WhenCentralRuleProviderIsSchemaScoped()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var ruleProvider = new SchemaScopedRuleProvider(excludedSchema: "dbo");

        const string dboValue = "dbo-clear-value";
        var dboFactory = new MockQueryConnectionFactory(new MockQueryRowConfig(
            dboValue, ColumnName: "Email", Origin: new MockSchemaOrigin(BaseSchemaName: "dbo", BaseTableName: "Kunden", BaseColumnName: "Email")));
        var dboService = BuildSchemaScopedService(options, dboFactory, ruleProvider: ruleProvider);

        var dboResult = await dboService.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Email FROM Kunden", null, TestContext.Current.CancellationToken);
        Assert.True(dboResult.IsSuccess);
        Assert.Contains(dboValue, dboResult.Value.Data, StringComparison.Ordinal);
        Assert.False(dboResult.Value.WasAnonymized);

        const string archivValue = "archiv-secret-value";
        var archivFactory = new MockQueryConnectionFactory(new MockQueryRowConfig(
            archivValue, ColumnName: "Email", Origin: new MockSchemaOrigin(BaseSchemaName: "Archiv", BaseTableName: "Kunden", BaseColumnName: "Email")));
        var archivService = BuildSchemaScopedService(options, archivFactory, ruleProvider: ruleProvider);

        var archivResult = await archivService.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Email FROM Kunden", null, TestContext.Current.CancellationToken);
        Assert.True(archivResult.IsSuccess);
        Assert.DoesNotContain(archivValue, archivResult.Value.Data, StringComparison.Ordinal);
        Assert.True(archivResult.Value.WasAnonymized);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldExcludeOnlyMatchingSchema_WhenDbExclusionIsSchemaScoped()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var exclusions = new AnonymizerExclusionSet([new AnonymizerExclusionEntry("dbo", "Kunden", "Email")]);
        var exclusionProvider = new FakeExclusionProvider(exclusions);

        const string dboValue = "dbo-clear-value";
        var dboFactory = new MockQueryConnectionFactory(new MockQueryRowConfig(
            dboValue, ColumnName: "Email", Origin: new MockSchemaOrigin(BaseSchemaName: "dbo", BaseTableName: "Kunden", BaseColumnName: "Email")));
        var dboService = BuildSchemaScopedService(options, dboFactory, exclusionProvider: exclusionProvider);

        var dboResult = await dboService.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Email FROM Kunden", null, TestContext.Current.CancellationToken);
        Assert.True(dboResult.IsSuccess);
        Assert.Contains(dboValue, dboResult.Value.Data, StringComparison.Ordinal);
        Assert.False(dboResult.Value.WasAnonymized);

        const string archivValue = "archiv-secret-value";
        var archivFactory = new MockQueryConnectionFactory(new MockQueryRowConfig(
            archivValue, ColumnName: "Email", Origin: new MockSchemaOrigin(BaseSchemaName: "Archiv", BaseTableName: "Kunden", BaseColumnName: "Email")));
        var archivService = BuildSchemaScopedService(options, archivFactory, exclusionProvider: exclusionProvider);

        var archivResult = await archivService.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Email FROM Kunden", null, TestContext.Current.CancellationToken);
        Assert.True(archivResult.IsSuccess);
        Assert.DoesNotContain(archivValue, archivResult.Value.Data, StringComparison.Ordinal);
        Assert.True(archivResult.Value.WasAnonymized);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldExcludeAllSchemas_WhenDbExclusionHasNoSchemaScope()
    {
        // Backward-compatibility regression: an exclusion with no schema qualifier (the historical
        // configuration shape, still supported) must keep applying across every schema, exactly as
        // before schema scoping existed.
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var exclusions = new AnonymizerExclusionSet([new AnonymizerExclusionEntry(null, "Kunden", "Email")]);
        var exclusionProvider = new FakeExclusionProvider(exclusions);

        foreach (string schema in new[] { "dbo", "Archiv" })
        {
            string value = $"{schema}-value";
            var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(
                value, ColumnName: "Email", Origin: new MockSchemaOrigin(BaseSchemaName: schema, BaseTableName: "Kunden", BaseColumnName: "Email")));
            var service = BuildSchemaScopedService(options, factory, exclusionProvider: exclusionProvider);

            var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Email FROM Kunden", null, TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Contains(value, result.Value.Data, StringComparison.Ordinal);
            Assert.False(result.Value.WasAnonymized);
        }
    }
}
