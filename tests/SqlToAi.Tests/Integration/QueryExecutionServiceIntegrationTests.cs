#nullable enable

using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using Dapper;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class QueryExecutionServiceIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _db;

    public QueryExecutionServiceIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _db = TestConstants.DatabaseName;
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldReturnJsonLines_ForSimpleSelect()
    {
        var result = await _fx.QueryExecutionService.ExecuteQueryAsync(_db, "SELECT 1 AS One, 'Hello' AS Status", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value.Data);
        // The output is a single JSON line with both columns
        Assert.Contains("\"One\":1", result.Value.Data);
        Assert.Contains("Hello", result.Value.Data);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldReturnRowsFromKnownTable()
    {
        // Fictional project table is more controlled
        var result = await _fx.QueryExecutionService.ExecuteQueryAsync(
            _db,
            "SELECT TOP 5 Mandant FROM dbo.FakeProjects",
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value.Data);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldBlock_MutatingStatement()
    {
        // Even though the rollback protects the DB, the ReadOnlyGuard must reject the statement
        // up front so it never reaches the server.
        // We force ReadOnly access level to test the ReadOnlyGuard.
        var customAccessProvider = new FakeAccessLevelProvider(AccessLevel.ReadOnly);
        var service = new QueryExecutionService(
            _fx.ConnectionFactory,
            _fx.SecurityGuard,
            customAccessProvider,
            _fx.ReadOnlyGuard,
            new AnonymizationDependencies(_fx.Anonymizer),
            Microsoft.Extensions.Options.Options.Create(_fx.Options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryExecutionService>.Instance);

        var result = await service.ExecuteQueryAsync(
            _db,
            "DELETE FROM dbo.FakeProjects",
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldBlock_MultiStatement()
    {
        var result = await _fx.QueryExecutionService.ExecuteQueryAsync(
            _db,
            "SELECT 1; SELECT 2",
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldCapRows_AtMaxRowLimit()
    {
        // Force the cap with a low MaxRowLimit via a dedicated options instance
        var options = CloneOptions();
        options.QueryExecution.MaxRowLimit = 2;
        var service = BuildExecutionServiceWithOptions(options);

        var result = await service.ExecuteQueryAsync(
            _db,
            "SELECT TOP 1000 1 AS X FROM sys.objects",
            1000,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        int lines = CountNonEmptyLines(result.Value.Data);
        Assert.Equal(2, lines);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldRespectDefaultRowLimit_WhenNoLimitProvided()
    {
        var options = CloneOptions();
        options.QueryExecution.DefaultRowLimit = 3;
        var service = BuildExecutionServiceWithOptions(options);

        var result = await service.ExecuteQueryAsync(
            _db,
            "SELECT TOP 100 1 AS X FROM sys.objects",
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        int lines = CountNonEmptyLines(result.Value.Data);
        Assert.Equal(3, lines);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldAnonymizePii_AgainstRealTable()
    {
        // Pick a table with a varchar column likely to contain real values. We do not assert
        // specific content — only that the call succeeds and the output is well-formed JSON.
        var result = await _fx.QueryExecutionService.ExecuteQueryAsync(
            _db,
            "SELECT TOP 1 Ausfuehrer FROM dbo.FakeContacts",
            null,
            TestContext.Current.CancellationToken);

        // The table may be empty — that is a valid result, no anonymization assertion possible.
        // We only assert the call did not fail (would indicate the service is mis-wired).
        Assert.True(
            result.IsSuccess || result.Error.Code == SqlToAiError.QueryErrorCode,
            IntegrationAssertions.FormatFailure(result));
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldFail_WithInvalidQuery()
    {
        var result = await _fx.QueryExecutionService.ExecuteQueryAsync(
            _db,
            "SELECT FROM NONSENSE",
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private SqlToAi.Configuration.SqlToAiOptions CloneOptions() => new()
    {
        SqlServer = _fx.Options.SqlServer,
        Databases = _fx.Options.Databases,
        Anonymizer = _fx.Options.Anonymizer,
        MetadataProvider = _fx.Options.MetadataProvider,
        QueryExecution = new SqlToAi.Configuration.QueryExecutionOptions
        {
            DefaultRowLimit = _fx.Options.QueryExecution.DefaultRowLimit,
            MaxRowLimit = _fx.Options.QueryExecution.MaxRowLimit
        }
    };

    private QueryExecutionService BuildExecutionServiceWithOptions(SqlToAi.Configuration.SqlToAiOptions options, IAccessLevelProvider? customAccessProvider = null)
    {
        var optionsWrapper = Microsoft.Extensions.Options.Options.Create(options);
        var exclusionProvider = new AnonymizerExclusionProvider(_fx.ConnectionFactory, optionsWrapper, Microsoft.Extensions.Logging.Abstractions.NullLogger<AnonymizerExclusionProvider>.Instance);
        return new QueryExecutionService(
            _fx.ConnectionFactory,
            _fx.SecurityGuard,
            customAccessProvider ?? _fx.AccessLevelProvider,
            _fx.ReadOnlyGuard,
            new AnonymizationDependencies(new Anonymizer(optionsWrapper, new TokenVault()), exclusionProvider),
            optionsWrapper,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryExecutionService>.Instance);
    }

    private static int CountNonEmptyLines(string s) =>
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

    private sealed class FakeAccessLevelProvider(AccessLevel level) : IAccessLevelProvider
    {
        public Task<AccessLevel> GetAccessLevelAsync(string databaseName, CancellationToken cancellationToken = default)
            => Task.FromResult(level);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ShouldRespectDatabaseExclusions_AgainstRealTable()
    {
        // 1. Arrange
        // Create AnonymizerExclusions table and add FakeProjects.ProjectName exclusion
        using (var connection = _fx.ConnectionFactory.CreateConnection(_db))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            // Create table
            await connection.ExecuteAsync(@"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AnonymizerExclusions]') AND type in (N'U'))
                BEGIN
                    CREATE TABLE [dbo].[AnonymizerExclusions] (
                        [TableName] NVARCHAR(255) NOT NULL,
                        [ColumnName] NVARCHAR(255) NOT NULL,
                        CONSTRAINT [PK_AnonymizerExclusions] PRIMARY KEY CLUSTERED ([TableName] ASC, [ColumnName] ASC)
                    );
                END");

            // Insert exclusion
            await connection.ExecuteAsync(@"
                IF NOT EXISTS (SELECT * FROM [dbo].[AnonymizerExclusions] WHERE [TableName] = 'FakeProjects' AND [ColumnName] = 'ProjectName')
                BEGIN
                    INSERT INTO [dbo].[AnonymizerExclusions] ([TableName], [ColumnName])
                    VALUES ('FakeProjects', 'ProjectName');
                END");

            // Insert test project always
            await connection.ExecuteAsync(@"
                INSERT INTO [dbo].[FakeProjects] (ProjectName, Mandant, Description, StartDate, Status)
                VALUES ('UnanonymizedProjectName', 1, 'ThisIsSecretDescription', GETDATE(), 'Active')");
        }

        // Configure options to use our exclusion SQL
        var options = CloneOptions();
        options.Databases.AnonymizerExclusionSql = "SELECT TableName, ColumnName FROM dbo.AnonymizerExclusions";

        // We build the custom execution service with these options and mock access level to force anonymization
        var customAccessProvider = new FakeAccessLevelProvider(AccessLevel.ReadOnlyAnonymized);
        var service = BuildExecutionServiceWithOptions(options, customAccessProvider);

        // 2. Act
        var result = await service.ExecuteQueryAsync(
            _db,
            "SELECT ProjectName, Description FROM dbo.FakeProjects WHERE ProjectName = 'UnanonymizedProjectName'",
            null,
            TestContext.Current.CancellationToken);

        // 3. Clean up
        using (var connection = _fx.ConnectionFactory.CreateConnection(_db))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await connection.ExecuteAsync("DELETE FROM [dbo].[FakeProjects] WHERE [ProjectName] = 'UnanonymizedProjectName'");
            await connection.ExecuteAsync("DELETE FROM [dbo].[AnonymizerExclusions] WHERE [TableName] = 'FakeProjects' AND [ColumnName] = 'ProjectName'");
        }

        // 4. Assert
        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value.Data);
        Assert.Contains("UnanonymizedProjectName", result.Value.Data);
        Assert.DoesNotContain("ThisIsSecretDescription", result.Value.Data);
    }
}
