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
/// Unit tests for <see cref="QueryExecutionService"/>.
/// Uses a test double for the connection factory and real implementations of the guards.
/// </summary>
public sealed class QueryExecutionServiceTests
{
    // -------------------------------------------------------------------------
    // Helpers: build service with configurable fakes
    // -------------------------------------------------------------------------

    private static QueryExecutionService BuildService(
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        bool isAllowed = true,
        bool readOnlySafe = true,
        string? mockData = null,
        SqlToAiOptions? options = null)
    {
        options ??= new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory(mockData ?? "Col1\tVal1");
        var securityGuard = new FakeSecurityGuard(isAllowed);
        var accessLevelProvider = new FakeAccessLevelProvider(accessLevel);
        var readOnlyGuard = new FakeReadOnlyGuard(readOnlySafe);
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());
        return new QueryExecutionService(
            factory, securityGuard, accessLevelProvider, readOnlyGuard,
            new AnonymizationDependencies(anonymizer), Options.Create(options), NullLogger<QueryExecutionService>.Instance);
    }

    // -------------------------------------------------------------------------
    // Tests: input validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFail_WhenDatabaseNameIsEmpty()
    {
        var service = BuildService();
        var result = await service.ExecuteQueryAsync("", "SELECT 1", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFail_WhenQueryIsEmpty()
    {
        var service = BuildService();
        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "   ", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests: security checks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFail_WhenDatabaseNotAllowed()
    {
        var service = BuildService(isAllowed: false);
        var result = await service.ExecuteQueryAsync("BlockedDb", "SELECT 1", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Theory]
    [InlineData(AccessLevel.None)]
    [InlineData(AccessLevel.SchemaOnly)]
    public async Task ExecuteQueryAsync_ShouldFail_WhenAccessLevelTooLow(AccessLevel level)
    {
        var service = BuildService(accessLevel: level);
        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFail_WhenQueryIsMutating()
    {
        var service = BuildService(readOnlySafe: false);
        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "DELETE FROM Customers", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests: multi-statement detection
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1 ; DROP TABLE Foo")]
    [InlineData("SELECT 'hello'; SELECT 'world'")]
    public async Task ExecuteQueryAsync_ShouldFail_WhenMultipleStatements(string query)
    {
        var service = BuildService();
        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, query, null, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT 1;")]           // trailing semicolon only — allowed
    [InlineData("SELECT 'hello;world'")] // semicolon inside string literal
    [InlineData("SELECT 1 -- note; comment")]
    public async Task ExecuteQueryAsync_ShouldSucceed_WhenSingleStatement(string query)
    {
        var service = BuildService();
        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, query, null, TestContext.Current.CancellationToken);
        // We only verify the multi-statement check passes; actual query execution may return stub data
        Assert.True(result.IsSuccess || result.Error.Code == SqlToAiError.QueryErrorCode);
    }

    // -------------------------------------------------------------------------
    // Tests: ReadWrite access level unlocks mutating statements (and commits them)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldAllowMutatingQuery_AndCommit_WhenReadWrite()
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory();
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadWrite),
            new FakeReadOnlyGuard(safe: false), // guard would reject it — must be bypassed
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "UPDATE Customers SET Name = 'X'", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.RollbackCount);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldStillRollBack_WhenAccessLevelIsNotReadWrite()
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory();
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly), // not ReadWrite
            new FakeReadOnlyGuard(safe: true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.RollbackCount);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldStillForbidMultipleStatements_WhenWriteAllowed()
    {
        var options = new SqlToAiOptions();
        var service = new QueryExecutionService(
            new MockQueryConnectionFactory(), new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadWrite),
            new FakeReadOnlyGuard(safe: true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "UPDATE Foo SET X=1; UPDATE Bar SET Y=2", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests: row limit enforcement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldRespectDefaultRowLimit()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 2, MaxRowLimit = 100 } };
        // MockQueryConnectionFactory returns 5 rows; default limit is 2
        var factory = new MockQueryConnectionFactory(null, rowCount: 5);
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        int lineCount = result.Value.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(2, lineCount);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldCapAtMaxRowLimit()
    {
        var options = new SqlToAiOptions { QueryExecution = new QueryExecutionOptions { DefaultRowLimit = 100, MaxRowLimit = 3 } };
        var factory = new MockQueryConnectionFactory(null, rowCount: 10);
        var service = new QueryExecutionService(
            factory, new FakeSecurityGuard(true), new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            new FakeReadOnlyGuard(true), new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT 1", 999, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        int lineCount = result.Value.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(3, lineCount);
    }

    // -------------------------------------------------------------------------
    // Tests: anonymization
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteQueryAsync_ShouldAnonymizeStrings_WhenReadOnlyAnonymized()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var factory = new MockQueryConnectionFactory(stringValue: "Ralf Huesing");
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
        var factory = new MockQueryConnectionFactory(stringValue: original);
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
        var factory = new MockQueryConnectionFactory(stringValue: "123-ABC");
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
        var factory = new MockQueryConnectionFactory(stringValue: "Ralf Huesing", baseTableName: "FakeConsultants");
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
        var factory = new MockQueryConnectionFactory(stringValue: original, baseTableName: "FakeConsultants");
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
        SqlToAiOptions options, string stringValue, string columnName = "IBAN")
    {
        var vault = new TokenVault();
        var factory = new MockQueryConnectionFactory(stringValue: stringValue, columnName: columnName);
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
        var factory = new MockQueryConnectionFactory(stringValue: "unused");
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
