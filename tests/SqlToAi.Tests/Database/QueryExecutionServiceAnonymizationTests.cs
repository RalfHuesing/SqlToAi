#nullable enable

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Anonymization and tokenization tests for <see cref="QueryExecutionService"/>. Split out of
/// <see cref="QueryExecutionServiceTests"/> (see that file for input-validation, security, and
/// row-limit tests) purely to stay within the project's per-file line-count budget — this is a
/// second partial-class file, not a separate test subject.
/// </summary>
public sealed partial class QueryExecutionServiceTests
{
    // -------------------------------------------------------------------------
    // Tests: anonymization
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldAnonymizeStrings_WhenReadOnlyAnonymized()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("Ralf Huesing"));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Name FROM Customers", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Ralf Huesing", result.Value.Data, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Value.WasAnonymized);
        Assert.Contains("Name", result.Value.AnonymizedColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldNotAnonymize_WhenReadOnly()
    {
        const string original = "Ralf Huesing";
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(original));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Name FROM Customers", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Contains(original, result.Value.Data, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.Value.WasAnonymized);
        Assert.Empty(result.Value.AnonymizedColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldNotReportAnonymization_WhenAllStringColumnsAreExcluded()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.ExcludedColumns = new List<string> { "Name" };
        // baseColumnName matches the alias here (the common, non-aliased case) so the resolved
        // origin equals "Name", same as before this test existed.
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("123-ABC", Origin: new MockSchemaOrigin(BaseColumnName: "Name")));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        // Since the column name is Name (which matches exclusion), it is not anonymized
        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Name FROM Customers", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Contains("123-ABC", result.Value.Data);
        Assert.False(result.Value.WasAnonymized);
        Assert.Empty(result.Value.AnonymizedColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldQualifyAnonymizedColumns_WithResolvedTableName()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("Ralf Huesing", Origin: new MockSchemaOrigin(BaseTableName: "FakeConsultants")));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Name FROM FakeConsultants", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WasAnonymized);
        Assert.Contains("FakeConsultants.Name", result.Value.AnonymizedColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldNotAnonymize_WhenCentralRuleProviderExcludesColumn()
    {
        const string original = "Ralf Huesing";
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(original, Origin: new MockSchemaOrigin(BaseTableName: "FakeConsultants")));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
            new FakeReadOnlyGuard(true),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault()), RuleProvider: new AlwaysExcludeRuleProvider()),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Name FROM FakeConsultants", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(original, result.Value.Data, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.Value.WasAnonymized);
        Assert.Empty(result.Value.AnonymizedColumns);
    }

    // -------------------------------------------------------------------------
    // Tests: alias-vs-origin exclusion decision (audit finding — see
    // tasks/audit-2026-07-24/01-security-guardrails.md, Finding 1). Reproduces
    // "SELECT SSN AS RecordId FROM Customers" with ExcludedColumns: ["*Id"].
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldAnonymize_WhenOutputAliasMatchesExclusion_ButRealSourceColumnDoesNot()
    {
        const string realSsn = "123-45-6789";
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.ExcludedColumns = new List<string> { "*Id" };
        // The reader reports the alias "RecordId" (matches "*Id"), but the schema table's
        // BaseColumnName says the real source column is "SSN" (does not match "*Id").
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(
            realSsn, ColumnName: "RecordId", Origin: new MockSchemaOrigin(BaseTableName: "Customers", BaseColumnName: "SSN")));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(
            TestConstants.DatabaseName, "SELECT SSN AS RecordId FROM Customers", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(realSsn, result.Value.Data, StringComparison.Ordinal);
        Assert.True(result.Value.WasAnonymized);
        Assert.Contains("Customers.RecordId", result.Value.AnonymizedColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldRespectExclusion_WhenAliasEqualsRealSourceColumnName()
    {
        // The common case: no aliasing, alias and resolved base column name are identical —
        // existing exclusion behavior for "*Id"-style patterns must be unchanged.
        const string original = "123";
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.ExcludedColumns = new List<string> { "*Id" };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(
            original, ColumnName: "CustomerId", Origin: new MockSchemaOrigin(BaseTableName: "Customers", BaseColumnName: "CustomerId")));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(
            TestConstants.DatabaseName, "SELECT CustomerId FROM Customers", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(original, result.Value.Data, StringComparison.Ordinal);
        Assert.False(result.Value.WasAnonymized);
        Assert.Empty(result.Value.AnonymizedColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFailSafeAndAnonymize_WhenRealSourceColumnIsUnresolvable_EvenIfAliasMatchesExclusion()
    {
        // No schema table available at all (e.g. a computed/literal/aggregate expression, or a
        // provider without schema-table support) — must never fall back to trusting the alias
        // against the plain pattern list, even though the alias "RecordId" would match "*Id".
        const string realValue = "SecretValue";
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.ExcludedColumns = new List<string> { "*Id" };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(
            realValue, ColumnName: "RecordId", Origin: new MockSchemaOrigin(Available: false)));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(
            TestConstants.DatabaseName, "SELECT SomeExpr AS RecordId FROM Customers", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(realValue, result.Value.Data, StringComparison.Ordinal);
        Assert.True(result.Value.WasAnonymized);
        Assert.Contains("RecordId", result.Value.AnonymizedColumns);
    }

    // -------------------------------------------------------------------------
    // Tests: searchable tokenization (egress + ingress)
    // -------------------------------------------------------------------------

    private static SqlToAiOptions BuildTokenizationOptions(bool enabled = true)
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.Tokenization.Enabled = enabled;
        options.Anonymizer.Tokenization.Secret = "top-secret";
        return options;
    }

    private static (QueryExecutionService Service, MockQueryConnectionFactory Factory, TokenVault Vault) BuildTokenizingService(
        SqlToAiOptions options, string stringValue, string columnName = "IBAN", string? baseColumnName = null)
    {
        var vault = new TokenVault();
        // Defaults the resolved origin column to the alias itself (the common, non-aliased case)
        // so pre-existing tests keep their original semantics; pass an explicit baseColumnName to
        // exercise the alias-vs-origin distinction deliberately.
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(
            stringValue, ColumnName: columnName, Origin: new MockSchemaOrigin(BaseColumnName: baseColumnName ?? columnName)));
        var anonymizer = new Anonymizer(Options.Create(options), vault);
        var resolver = new QueryTokenResolver(vault, Options.Create(options));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
            new FakeReadOnlyGuard(true),
            new AnonymizationDependencies(anonymizer, TokenResolver: resolver),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);
        return (service, factory, vault);
    }

    /// <summary>
    /// Deserializes the first JSONL row and returns a column's real string value — the result is
    /// what an AI actually sees after JSON-decoding, unlike scanning the raw (Unicode-escaped) text.
    /// </summary>
    private static string ExtractColumnValue(string jsonLinesData, string columnName)
    {
        string firstLine = jsonLinesData.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        using var doc = JsonDocument.Parse(firstLine);
        return doc.RootElement.GetProperty(columnName).GetString()!;
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldTokenizeEveryAnonymizedColumn_WhenTokenizationEnabled()
    {
        // No column-name configuration anywhere — tokenization is a blanket mode switch, exactly
        // like DefaultMode, so an arbitrarily named column gets tokenized automatically.
        const string realValue = "DE89370400440532013000";
        var options = BuildTokenizationOptions();
        var (service, _, vault) = BuildTokenizingService(options, realValue, columnName: "SomeArbitraryColumn");

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT SomeArbitraryColumn FROM Whatever", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(realValue, result.Value.Data, StringComparison.Ordinal);
        Assert.True(result.Value.WasAnonymized);

        string token = ExtractColumnValue(result.Value.Data, "SomeArbitraryColumn");
        Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, token, StringComparison.Ordinal);
        Assert.True(vault.TryResolve(token, out string? resolved));
        Assert.Equal(realValue, resolved);

        Assert.Contains("SomeArbitraryColumn", result.Value.SearchableTokenColumns);
        Assert.Contains("SomeArbitraryColumn", result.Value.AnonymizedColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldUseRegularMasking_WhenTokenizationDisabled()
    {
        const string original = "Ralf Huesing";
        var options = BuildTokenizationOptions(enabled: false);
        var (service, _, _) = BuildTokenizingService(options, original, columnName: "Name");

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Name FROM Customers", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("§§§", result.Value.Data, StringComparison.Ordinal);
        Assert.DoesNotContain(original, result.Value.Data, StringComparison.Ordinal); // still masked, just not tokenized
        Assert.True(result.Value.WasAnonymized);
        Assert.Empty(result.Value.SearchableTokenColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldRespectExistingExclusions_EvenWhenTokenizationEnabled()
    {
        // Exclusion mechanisms (ExcludedColumns here) still take precedence — tokenization only
        // changes *how* an already-anonymized column is anonymized, never *whether* it is.
        const string original = "Active";
        var options = BuildTokenizationOptions();
        options.Anonymizer.ExcludedColumns = new List<string> { "Status" };
        var (service, _, _) = BuildTokenizingService(options, original, columnName: "Status");

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Status FROM Orders", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(original, result.Value.Data, StringComparison.Ordinal);
        Assert.False(result.Value.WasAnonymized);
        Assert.Empty(result.Value.SearchableTokenColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldStillTokenize_WhenOutputAliasMatchesExclusion_ButRealSourceColumnDoesNot()
    {
        // Mirrors the plain-Anonymize regression above, but for the tokenization path: a
        // previously issued token gets resolved back to its real value by QueryTokenResolver
        // before execution, and that resolved literal then flows through this same (alias-based)
        // column as "SomeId" — the alias must not let the real IBAN escape in clear text either.
        const string realIban = "DE89370400440532013000";
        var options = BuildTokenizationOptions();
        options.Anonymizer.ExcludedColumns = new List<string> { "*Id" };
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(
            realIban, ColumnName: "SomeId", Origin: new MockSchemaOrigin(BaseTableName: "Accounts", BaseColumnName: "IBAN")));
        var vault = new TokenVault();
        var anonymizer = new Anonymizer(Options.Create(options), vault);
        var resolver = new QueryTokenResolver(vault, Options.Create(options));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(anonymizer, TokenResolver: resolver),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(
            TestConstants.DatabaseName, "SELECT IBAN AS SomeId FROM Accounts", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(realIban, result.Value.Data, StringComparison.Ordinal);
        Assert.True(result.Value.WasAnonymized);
        Assert.Contains("Accounts.SomeId", result.Value.AnonymizedColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldResolveToken_BackToRealValue_BeforeExecution()
    {
        var options = BuildTokenizationOptions();
        var (service, factory, vault) = BuildTokenizingService(options, "unused");
        vault.Store("§§§preissued§§§", "DE89370400440532013000");

        var result = await service.ExecuteQueryAsync(
            TestConstants.DatabaseName, "SELECT * FROM Accounts WHERE IBAN = '§§§preissued§§§'", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "SELECT * FROM Accounts WHERE IBAN = 'DE89370400440532013000'",
            factory.LastConnection?.LastCommand?.CommandText);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldLeaveUnknownToken_UnresolvedInExecutedQuery()
    {
        var options = BuildTokenizationOptions();
        var (service, factory, _) = BuildTokenizingService(options, "unused");

        await service.ExecuteQueryAsync(
            TestConstants.DatabaseName, "SELECT * FROM Accounts WHERE IBAN = '§§§forged§§§'", null, TestContext.Current.CancellationToken);

        Assert.Equal(
            "SELECT * FROM Accounts WHERE IBAN = '§§§forged§§§'",
            factory.LastConnection?.LastCommand?.CommandText);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldNotResolveTokens_WhenAccessLevelIsNotAnonymized()
    {
        var options = BuildTokenizationOptions();
        var vault = new TokenVault();
        vault.Store("§§§preissued§§§", "DE89370400440532013000");
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("unused"));
        var anonymizer = new Anonymizer(Options.Create(options), vault);
        var resolver = new QueryTokenResolver(vault, Options.Create(options));
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly), // plain ReadOnly, not anonymized
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(anonymizer, TokenResolver: resolver),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        await service.ExecuteQueryAsync(
            TestConstants.DatabaseName, "SELECT * FROM Accounts WHERE IBAN = '§§§preissued§§§'", null, TestContext.Current.CancellationToken);

        Assert.Equal(
            "SELECT * FROM Accounts WHERE IBAN = '§§§preissued§§§'",
            factory.LastConnection?.LastCommand?.CommandText);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldRoundTripToken_FromEgressQueryIntoIngressQuery()
    {
        const string iban = "DE89370400440532013000";
        var options = BuildTokenizationOptions();
        var (service, factory, _) = BuildTokenizingService(options, iban);

        // 1. Egress: a first query hands the AI a token instead of the real IBAN.
        var first = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT IBAN FROM Accounts", null, TestContext.Current.CancellationToken);
        string token = ExtractColumnValue(first.Value.Data, "IBAN");

        // 2. Ingress: the AI reuses that exact token in a follow-up query.
        await service.ExecuteQueryAsync(
            TestConstants.DatabaseName, $"SELECT * FROM Accounts WHERE IBAN = '{token}'", null, TestContext.Current.CancellationToken);

        Assert.Equal(
            $"SELECT * FROM Accounts WHERE IBAN = '{iban}'",
            factory.LastConnection?.LastCommand?.CommandText);
    }
}
