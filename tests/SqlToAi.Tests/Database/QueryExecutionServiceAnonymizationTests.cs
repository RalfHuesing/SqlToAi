#nullable enable

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Anonymization and tokenization tests for <see cref="QueryExecutionService"/>.
/// </summary>
public sealed class QueryExecutionServiceAnonymizationTests
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
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Name FROM Customers", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Ralf Huesing", result.Value.Data, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Value.WasAnonymized);
        Assert.Contains("Name", result.Value.AnonymizedColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldAnonymizeStrings_WhenQueryContainsDeclare()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("Ralf Huesing"));
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        const string declareQuery = "DECLARE @Id int = 1; SELECT Name FROM Customers WHERE Id = @Id";
        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, declareQuery, null, TestContext.Current.CancellationToken);
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
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnly),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Name FROM Customers", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Contains(original, result.Value.Data, StringComparison.OrdinalIgnoreCase);
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
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
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
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
                new FakeReadOnlyGuard(true)),
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
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
                new FakeReadOnlyGuard(true)),
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
        var options = AnonymizationTestHelper.BuildTokenizationOptions();
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
        var options = AnonymizationTestHelper.BuildTokenizationOptions(enabled: false);
        var (service, _, _) = BuildTokenizingService(options, original, columnName: "Name");

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT Name FROM Customers", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("§§§", result.Value.Data, StringComparison.Ordinal);
        Assert.DoesNotContain(original, result.Value.Data, StringComparison.Ordinal); // still masked, just not tokenized
        Assert.True(result.Value.WasAnonymized);
        Assert.Empty(result.Value.SearchableTokenColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldResolveToken_BackToRealValue_BeforeExecution()
    {
        var options = AnonymizationTestHelper.BuildTokenizationOptions();
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
        var options = AnonymizationTestHelper.BuildTokenizationOptions();
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
        var options = AnonymizationTestHelper.BuildTokenizationOptions();
        var vault = new TokenVault();
        vault.Store("§§§preissued§§§", "DE89370400440532013000");
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig("unused"));
        var anonymizer = new Anonymizer(Options.Create(options), vault);
        var resolver = new QueryTokenResolver(vault, Options.Create(options));
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnly), // plain ReadOnly, not anonymized
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(anonymizer, TokenResolver: resolver),
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
        var options = AnonymizationTestHelper.BuildTokenizationOptions();
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

    // -------------------------------------------------------------------------
    // Tests: filtering the raw exception message returned to the AI on execution
    // failure (audit finding — see tasks/audit-2026-07-24/01-security-guardrails.md and
    // 02-anonymisierung-tokenisierung.md, "Detokenisierte Klartextwerte leaken über
    // Fehlerpfad"). The log file deliberately still gets the full, untouched message — only
    // what goes back to the AI as the tool's error response changes, and only when the
    // database is anonymized/tokenized.
    // -------------------------------------------------------------------------

    private const string SensitiveMarker = "secret-value-xyz";

    private static InvalidOperationException BuildSensitiveException() =>
        new($"Conversion failed when converting the varchar value '{SensitiveMarker}' to data type int.");

    [Fact]
    public async Task ExecuteQueryAsync_ShouldNotLeakRawExceptionMessage_ToAi_WhenAnonymizedAndExecutionThrows()
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(ThrowOnExecute: BuildSensitiveException()));
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT * FROM Accounts WHERE AccountRef = '1'", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.DoesNotContain(SensitiveMarker, result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ExecuteQueryAsync_ShouldKeepRawExceptionMessage_ToAi_WhenNotAnonymizedAndExecutionThrows(AccessLevel accessLevel)
    {
        var options = new SqlToAiOptions();
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(ThrowOnExecute: BuildSensitiveException()));
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(accessLevel),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT * FROM Accounts WHERE AccountRef = '1'", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains(SensitiveMarker, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldStillLogFullExceptionMessage_EvenWhenAnonymizedAndFilteredFromAiResponse()
    {
        // The owner's explicit, deliberate decision: the log file path is unchanged and must
        // keep receiving the full, untouched exception — only the AI-facing message is filtered.
        var options = new SqlToAiOptions();
        var sensitiveException = BuildSensitiveException();
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(ThrowOnExecute: sensitiveException));
        var logger = new CapturingLogger<QueryExecutionService>();
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), logger);

        var result = await service.ExecuteQueryAsync(TestConstants.DatabaseName, "SELECT * FROM Accounts WHERE AccountRef = '1'", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain(SensitiveMarker, result.Error.Message, StringComparison.Ordinal);
        // ...but the logger still received the exact same exception, untouched.
        Assert.Same(sensitiveException, logger.LastException);
        Assert.Contains(SensitiveMarker, logger.LastException?.Message, StringComparison.Ordinal);
    }
}
