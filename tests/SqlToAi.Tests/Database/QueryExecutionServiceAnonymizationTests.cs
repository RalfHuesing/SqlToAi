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
        var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
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

        var result = await service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName, "SELECT Name FROM Customers", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Ralf Huesing", result.Value.Data, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Value.WasAnonymized);
        Assert.Contains("Name", result.Value.AnonymizedColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldCommitWritesAndAnonymizeOutput_WhenReadWriteAnonymized()
    {
        const string original = "Ralf Huesing";
        var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
        options.Anonymizer.Enabled = true;
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(original,
            Origin: new MockSchemaOrigin(BaseTableName: "Customers", BaseColumnName: "Name", BaseSchemaName: "dbo")));
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadWriteAnonymized),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName,
            "INSERT INTO dbo.Customers (Name) OUTPUT inserted.Name VALUES ('Ralf Huesing')", null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(original, result.Value.Data, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Value.WasAnonymized);
        Assert.Contains("Customers.Name", result.Value.AnonymizedColumns);
        Assert.Equal(1, factory.LastConnection?.LastTransaction?.CommitCount);
        Assert.Equal(0, factory.LastConnection?.LastTransaction?.RollbackCount);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldAnonymizeStrings_WhenQueryContainsDeclare()
    {
        var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
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
        var result = await service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName, declareQuery, null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Ralf Huesing", result.Value.Data, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Value.WasAnonymized);
        Assert.Contains("Name", result.Value.AnonymizedColumns);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldNotAnonymize_WhenReadOnly()
    {
        const string original = "Ralf Huesing";
        var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
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

        var result = await service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName, "SELECT Name FROM Customers", null, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Contains(original, result.Value.Data, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.Value.WasAnonymized);
        Assert.Empty(result.Value.AnonymizedColumns);
    }



    [Fact]
    public async Task ExecuteQueryAsync_ShouldQualifyAnonymizedColumns_WithResolvedTableName()
    {
        var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
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

        var result = await service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName, "SELECT Name FROM FakeConsultants", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WasAnonymized);
        Assert.Contains("FakeConsultants.Name", result.Value.AnonymizedColumns);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnlyAnonymized)]
    [InlineData(AccessLevel.ReadWriteAnonymized)]
    public async Task ExecuteQueryAsync_ShouldNotAnonymize_WhenCentralRuleProviderExcludesColumn(AccessLevel accessLevel)
    {
        const string original = "Ralf Huesing";
        var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
        options.Anonymizer.Enabled = true;
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(original, Origin: new MockSchemaOrigin(
            BaseTableName: "FakeConsultants", BaseColumnName: "Name", BaseSchemaName: "dbo")));
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(accessLevel),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault()), RuleProvider: new AlwaysExcludeRuleProvider()),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName, "SELECT Name FROM FakeConsultants", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(original, result.Value.Data, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.Value.WasAnonymized);
        Assert.Empty(result.Value.AnonymizedColumns);
    }

    [Theory]
    [InlineData("FullName", false)]
    [InlineData("SSN", true)]
    public async Task ExecuteQueryAsync_CentralExclusionUsesSourceColumnInsteadOfAlias(string excludedColumn, bool expectedRaw)
    {
        const string original = "synthetic-secret-123";
        var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
        options.Anonymizer.Enabled = true;
        var ruleProvider = new ColumnNameRuleProvider(excludedColumn);
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(original,
            ColumnName: "FullName",
            Origin: new MockSchemaOrigin(BaseTableName: "FakeConsultants", BaseColumnName: "SSN", BaseSchemaName: "dbo")));
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault()), RuleProvider: ruleProvider),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(
            TestConstants.QualifiedDatabaseName, "SELECT SSN AS FullName FROM FakeConsultants", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedRaw, result.Value.Data.Contains(original, StringComparison.Ordinal));
        Assert.Equal([("dbo", "FakeConsultants", "SSN")], ruleProvider.Lookups);
    }

    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData(null, null, null, true)]
    [InlineData(null, "FakeConsultants", "SSN", false)]
    [InlineData(null, "FakeConsultants", "SSN", true)]
    [InlineData("dbo", null, "SSN", false)]
    [InlineData("dbo", null, "SSN", true)]
    [InlineData("dbo", "FakeConsultants", null, false)]
    [InlineData("dbo", "FakeConsultants", null, true)]
    public async Task ExecuteQueryAsync_KeepsDefaultMaskingWhenOriginIsPartial(
        string? schemaName, string? tableName, string? columnName, bool tokenizationEnabled)
    {
        const string original = "synthetic-secret-123";
        var options = tokenizationEnabled
            ? AnonymizationTestHelper.BuildTokenizationOptions()
            : new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
        options.Anonymizer.Enabled = true;
        var ruleProvider = new ColumnNameRuleProvider("FullName");
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(original,
            ColumnName: "FullName",
            Origin: new MockSchemaOrigin(tableName, columnName, BaseSchemaName: schemaName)));
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault()), RuleProvider: ruleProvider),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(
            TestConstants.QualifiedDatabaseName, "SELECT SSN AS FullName FROM FakeConsultants", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(original, result.Value.Data, StringComparison.Ordinal);
        Assert.Empty(ruleProvider.Lookups);
        if (tokenizationEnabled)
        {
            Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, ExtractColumnValue(result.Value.Data, "FullName"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ExecuteQueryAsync_KeepsDefaultMaskingWhenSchemaTableIsUnavailable()
    {
        const string original = "synthetic-secret-123";
        var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
        options.Anonymizer.Enabled = true;
        var ruleProvider = new ColumnNameRuleProvider("FullName");
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(original,
            ColumnName: "FullName", Origin: new MockSchemaOrigin(Available: false)));
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault()), RuleProvider: ruleProvider),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(
            TestConstants.QualifiedDatabaseName, "SELECT SSN AS FullName FROM FakeConsultants", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(original, result.Value.Data, StringComparison.Ordinal);
        Assert.Empty(ruleProvider.Lookups);
    }

    [Fact]
    public async Task ExecuteFileAsync_UsesSourceOriginForAliasMaskingAndTokenization()
    {
        const string original = "DE89370400440532013000";
        var options = AnonymizationTestHelper.BuildTokenizationOptions().WithNamedServers(TestTargets.ServerAlias);
        var ruleProvider = new ColumnNameRuleProvider("FullName");
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(original,
            ColumnName: "FullName",
            Origin: new MockSchemaOrigin(BaseTableName: "FakeConsultants", BaseColumnName: "SSN", BaseSchemaName: "dbo")));
        var validator = new FakeQuerySafetyValidator(
            new FakeSecurityGuard(true),
            new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
            new FakeReadOnlyGuard(true));
        var queryService = new QueryExecutionService(
            factory,
            validator,
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault()), RuleProvider: ruleProvider),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);
        var scriptService = new ScriptExecutionService(
            factory, validator, queryService, Options.Create(options), NullLogger<ScriptExecutionService>.Instance);

        var result = await scriptService.ExecuteAsync(
            new ScriptExecutionRequest(
                new SqlScriptFile("script.sql", "SELECT SSN AS FullName FROM FakeConsultants", "UTF-8"),
                TestConstants.QualifiedDatabaseName),
            TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Success, result.Status);
        string data = Assert.Single(Assert.Single(result.Batches).Executions).Data;
        Assert.DoesNotContain(original, data, StringComparison.Ordinal);
        Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, ExtractColumnValue(data, "FullName"), StringComparison.Ordinal);
        Assert.Equal([("dbo", "FakeConsultants", "SSN")], ruleProvider.Lookups);
    }

    [Fact]
    public async Task ExecuteFileAsync_LeavesSourceExemptionRawThroughOutputAlias()
    {
        const string original = "DE89370400440532013000";
        var options = AnonymizationTestHelper.BuildTokenizationOptions().WithNamedServers(TestTargets.ServerAlias);
        var ruleProvider = new ColumnNameRuleProvider("SSN");
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(original,
            ColumnName: "FullName",
            Origin: new MockSchemaOrigin(BaseTableName: "FakeConsultants", BaseColumnName: "SSN", BaseSchemaName: "dbo")));
        var validator = new FakeQuerySafetyValidator(
            new FakeSecurityGuard(true),
            new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
            new FakeReadOnlyGuard(true));
        var queryService = new QueryExecutionService(
            factory,
            validator,
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault()), RuleProvider: ruleProvider),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);
        var scriptService = new ScriptExecutionService(
            factory, validator, queryService, Options.Create(options), NullLogger<ScriptExecutionService>.Instance);

        var result = await scriptService.ExecuteAsync(
            new ScriptExecutionRequest(
                new SqlScriptFile("script.sql", "SELECT SSN AS FullName FROM FakeConsultants", "UTF-8"),
                TestConstants.QualifiedDatabaseName),
            TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Success, result.Status);
        string data = Assert.Single(Assert.Single(result.Batches).Executions).Data;
        Assert.Equal(original, ExtractColumnValue(data, "FullName"));
        Assert.DoesNotContain(ruleProvider.Lookups, lookup => lookup.Column != "SSN");
        Assert.Equal([("dbo", "FakeConsultants", "SSN")], ruleProvider.Lookups);
    }



    // -------------------------------------------------------------------------
    // Tests: searchable tokenization (egress + ingress)
    // -------------------------------------------------------------------------

    private static (QueryExecutionService Service, MockQueryConnectionFactory Factory, TokenVault Vault) BuildTokenizingService(
        SqlToAiOptions options, string stringValue, string columnName = "IBAN", string? baseColumnName = null,
        AccessLevel accessLevel = AccessLevel.ReadOnlyAnonymized)
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
                new FakeAccessLevelProvider(accessLevel),
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

    [Theory]
    [InlineData(AccessLevel.ReadOnlyAnonymized)]
    [InlineData(AccessLevel.ReadWriteAnonymized)]
    public async Task ExecuteQueryAsync_ShouldTokenizeEveryAnonymizedColumn_WhenTokenizationEnabled(AccessLevel accessLevel)
    {
        // No column-name configuration anywhere — tokenization is a blanket mode switch, exactly
        // like DefaultMode, so an arbitrarily named column gets tokenized automatically.
        const string realValue = "DE89370400440532013000";
        var options = AnonymizationTestHelper.BuildTokenizationOptions();
        var (service, _, vault) = BuildTokenizingService(options, realValue, columnName: "SomeArbitraryColumn", accessLevel: accessLevel);

        var result = await service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName, "SELECT SomeArbitraryColumn FROM Whatever", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(realValue, result.Value.Data, StringComparison.Ordinal);
        Assert.True(result.Value.WasAnonymized);

        string token = ExtractColumnValue(result.Value.Data, "SomeArbitraryColumn");
        Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, token, StringComparison.Ordinal);
        var target = TestTargets.ResolveTarget(options, TestConstants.QualifiedDatabaseName);
        Assert.True(vault.TryResolve(token, target, out string? resolved));
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

        var result = await service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName, "SELECT Name FROM Customers", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("§§§", result.Value.Data, StringComparison.Ordinal);
        Assert.DoesNotContain(original, result.Value.Data, StringComparison.Ordinal); // still masked, just not tokenized
        Assert.True(result.Value.WasAnonymized);
        Assert.Empty(result.Value.SearchableTokenColumns);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnlyAnonymized)]
    [InlineData(AccessLevel.ReadWriteAnonymized)]
    public async Task ExecuteQueryAsync_ShouldResolveToken_BackToRealValue_BeforeExecution(AccessLevel accessLevel)
    {
        var options = AnonymizationTestHelper.BuildTokenizationOptions();
        var (service, factory, vault) = BuildTokenizingService(options, "unused", accessLevel: accessLevel);
        var target = TestTargets.ResolveTarget(options, TestConstants.QualifiedDatabaseName);
        vault.Store("§§§preissued§§§", "DE89370400440532013000", target);

        var result = await service.ExecuteQueryAsync(
            TestConstants.QualifiedDatabaseName, "SELECT * FROM Accounts WHERE IBAN = '§§§preissued§§§'", null, TestContext.Current.CancellationToken);

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
            TestConstants.QualifiedDatabaseName, "SELECT * FROM Accounts WHERE IBAN = '§§§forged§§§'", null, TestContext.Current.CancellationToken);

        Assert.Equal(
            "SELECT * FROM Accounts WHERE IBAN = '§§§forged§§§'",
            factory.LastConnection?.LastCommand?.CommandText);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldNotResolveTokens_WhenAccessLevelIsNotAnonymized()
    {
        var options = AnonymizationTestHelper.BuildTokenizationOptions();
        var vault = new TokenVault();
        var target = TestTargets.ResolveTarget(options, TestConstants.QualifiedDatabaseName);
        vault.Store("§§§preissued§§§", "DE89370400440532013000", target);
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
            TestConstants.QualifiedDatabaseName, "SELECT * FROM Accounts WHERE IBAN = '§§§preissued§§§'", null, TestContext.Current.CancellationToken);

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
        var first = await service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName, "SELECT IBAN FROM Accounts", null, TestContext.Current.CancellationToken);
        string token = ExtractColumnValue(first.Value.Data, "IBAN");

        // 2. Ingress: the AI reuses that exact token in a follow-up query.
        await service.ExecuteQueryAsync(
            TestConstants.QualifiedDatabaseName, $"SELECT * FROM Accounts WHERE IBAN = '{token}'", null, TestContext.Current.CancellationToken);

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
        var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(ThrowOnExecute: BuildSensitiveException()));
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName, "SELECT * FROM Accounts WHERE AccountRef = '1'", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.DoesNotContain(SensitiveMarker, result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ExecuteQueryAsync_ShouldKeepRawExceptionMessage_ToAi_WhenNotAnonymizedAndExecutionThrows(AccessLevel accessLevel)
    {
        var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
        var factory = new MockQueryConnectionFactory(new MockQueryRowConfig(ThrowOnExecute: BuildSensitiveException()));
        var service = new QueryExecutionService(
            factory,
            new FakeQuerySafetyValidator(
                new FakeSecurityGuard(true),
                new FakeAccessLevelProvider(accessLevel),
                new FakeReadOnlyGuard(true)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName, "SELECT * FROM Accounts WHERE AccountRef = '1'", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains(SensitiveMarker, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldStillLogFullExceptionMessage_EvenWhenAnonymizedAndFilteredFromAiResponse()
    {
        // The owner's explicit, deliberate decision: the log file path is unchanged and must
        // keep receiving the full, untouched exception — only the AI-facing message is filtered.
        var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
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

        var result = await service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName, "SELECT * FROM Accounts WHERE AccountRef = '1'", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain(SensitiveMarker, result.Error.Message, StringComparison.Ordinal);
        // ...but the logger still received the exact same exception, untouched.
        Assert.Same(sensitiveException, logger.LastException);
        Assert.Contains(SensitiveMarker, logger.LastException?.Message, StringComparison.Ordinal);
    }
}
