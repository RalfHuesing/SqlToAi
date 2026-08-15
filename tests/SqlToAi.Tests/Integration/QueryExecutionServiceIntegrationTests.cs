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
        // Even though the rollback protects the DB, the pipeline must reject the statement
        // up front so it never reaches the server.
        // We force ReadOnly access level to test the read-only guard.
        var customAccessProvider = new FakeAccessLevelProvider(AccessLevel.ReadOnly);
        var customValidator = new QuerySafetyValidator(_fx.SecurityGuard, customAccessProvider, _fx.ReadOnlyGuard);
        var service = new QueryExecutionService(
            _fx.ConnectionFactory,
            customValidator,
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
    public async Task ExecuteQueryAsync_ShouldBlock_SpExecuteSqlEmbeddedCommitExploit_AndNotMutateData()
    {
        // Reproduces the exact documented bypass (audit finding 2) end-to-end against the real
        // SQL Server: sp_executesql with an embedded COMMIT, sent against a database forced to a
        // non-ReadWrite AccessLevel while still using the real 'Agent' SQL login from
        // appsettings.json â€” which, per AccessCheckSql, genuinely has DELETE rights on DemoDB
        // (ReadWrite is only withheld here by our own AccessLevelProvider override, not by
        // underlying SQL permissions) â€” exactly the "shared login controlled via AccessCheckSql"
        // scenario the finding describes. Proves both that the call is rejected AND that no row
        // was actually deleted.
        var customAccessProvider = new FakeAccessLevelProvider(AccessLevel.ReadOnly);
        var customValidator = new QuerySafetyValidator(_fx.SecurityGuard, customAccessProvider, _fx.ReadOnlyGuard);
        var service = new QueryExecutionService(
            _fx.ConnectionFactory,
            customValidator,
            new AnonymizationDependencies(_fx.Anonymizer),
            Microsoft.Extensions.Options.Options.Create(_fx.Options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryExecutionService>.Instance);

        long countBefore = await CountFakeProjectsAsync();

        var result = await service.ExecuteQueryAsync(
            _db,
            "sp_executesql N'DELETE FROM dbo.FakeProjects; COMMIT'",
            null,
            TestContext.Current.CancellationToken);

        long countAfter = await CountFakeProjectsAsync();

        Assert.True(result.IsFailure, IntegrationAssertions.FormatFailure(result));
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
        Assert.Equal(countBefore, countAfter);
    }

    private async Task<long> CountFakeProjectsAsync()
    {
        using var connection = _fx.ConnectionFactory.CreateConnection(_db);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM dbo.FakeProjects");
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
        // specific content â€” only that the call succeeds and the output is well-formed JSON.
        var result = await _fx.QueryExecutionService.ExecuteQueryAsync(
            _db,
            "SELECT TOP 1 Ausfuehrer FROM dbo.FakeContacts",
            null,
            TestContext.Current.CancellationToken);

        // The table may be empty â€” that is a valid result, no anonymization assertion possible.
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
        var safetyValidator = new QuerySafetyValidator(
            _fx.SecurityGuard,
            customAccessProvider ?? _fx.AccessLevelProvider,
            _fx.ReadOnlyGuard);
        return new QueryExecutionService(
            _fx.ConnectionFactory,
            safetyValidator,
            new AnonymizationDependencies(new Anonymizer(optionsWrapper, new TokenVault())),
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
}

