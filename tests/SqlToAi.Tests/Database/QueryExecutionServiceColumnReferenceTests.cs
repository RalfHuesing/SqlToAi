using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Tests.Mcp;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class QueryExecutionServiceColumnReferenceTests
{
    [Theory]
    [InlineData("SELECT 'hello' AS Msg", "Msg", "hello", false)]
    [InlineData("SELECT 'hello' AS Msg", "Msg", "hello", true)]
    [InlineData("SELECT 'hello' AS Msg FROM dbo.T", "Msg", "hello", false)]
    [InlineData("SELECT 'hello' AS Msg FROM dbo.T", "Msg", "hello", true)]
    [InlineData("SELECT @@LANGUAGE AS LanguageName", "LanguageName", "Deutsch", false)]
    [InlineData("SELECT @@LANGUAGE AS LanguageName", "LanguageName", "Deutsch", true)]
    [InlineData("SELECT 'ProtectedName *' AS Msg /* ProtectedName */", "Msg", "ProtectedName *", true)]
    public async Task ExecuteQueryAsync_ReturnsColumnIndependentStringsWithoutNotice(
        string query, string column, string value, bool tokenize)
    {
        var fixture = Create(new FakeDbResultSet([column], [[value]]), tokenize);

        var result = await fixture.Service.ExecuteQueryAsync(
            TestConstants.QualifiedDatabaseName, query, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value.Data);
        Assert.Equal(value, json.RootElement.GetProperty(column).GetString());
        Assert.False(result.Value.WasAnonymized);
        Assert.Empty(result.Value.AnonymizedColumns);
        Assert.Empty(result.Value.SearchableTokenColumns);
        await AssertNoNoticeAsync(fixture.Service, query, result.Value.Data);
    }

    [Fact]
    public async Task ExecuteQueryAsync_PreservesMixedLiteralTypes()
    {
        var fixture = Create(new FakeDbResultSet(["Msg", "N"], [["hello", 1]]), tokenize: true);

        var result = await fixture.Service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName,
            "SELECT 'hello' AS Msg, 1 AS N", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value.Data);
        Assert.Equal("hello", json.RootElement.GetProperty("Msg").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("N").GetInt32());
        Assert.False(result.Value.WasAnonymized);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.T", true, false)]
    [InlineData("SELECT * FROM dbo.T", true, true)]
    [InlineData("SELECT t.* FROM dbo.T AS t", true, false)]
    [InlineData("SELECT t.* FROM dbo.T AS t", true, true)]
    [InlineData("SELECT UPPER(ProtectedName) AS Value FROM dbo.T", false, false)]
    [InlineData("SELECT UPPER(ProtectedName) AS Value FROM dbo.T", false, true)]
    [InlineData("SELECT 'hello' AS Value FROM dbo.T WHERE ProtectedName LIKE '%'", false, false)]
    [InlineData("SELECT 'hello' AS Value FROM dbo.T WHERE ProtectedName LIKE '%'", false, true)]
    public async Task ExecuteQueryAsync_KeepsMaskingWhenAnyColumnIsReferenced(
        string query, bool hasOrigin, bool tokenize)
    {
        string original = query.Contains("'hello'", StringComparison.Ordinal) ? "hello" : "SYNTHETIC-SECRET";
        var origin = hasOrigin ? new FakeSchemaTableOrigin("T", "ProtectedName", "dbo") : null;
        var fixture = Create(new FakeDbResultSet(["Value"], [[original]], origin), tokenize);

        var result = await fixture.Service.ExecuteQueryAsync(
            TestConstants.QualifiedDatabaseName, query, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WasAnonymized);
        Assert.DoesNotContain(original, result.Value.Data, StringComparison.Ordinal);
        Assert.Single(fixture.Factory.ExecutedQueries);
        if (tokenize)
        {
            Assert.Single(result.Value.SearchableTokenColumns);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteQueryAsync_KeepsMaskingOnParseFailure(bool tokenize)
    {
        const string query = "SELECT 'hello' AS Msg FROM";
        Assert.False(SqlScriptDomParser.Parse(query).Success);
        var fixture = Create(new FakeDbResultSet(["Msg"], [["hello"]]), tokenize, bypassGuard: true);

        var result = await fixture.Service.ExecuteQueryAsync(
            TestConstants.QualifiedDatabaseName, query, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WasAnonymized);
        Assert.DoesNotContain("hello", result.Value.Data, StringComparison.Ordinal);
        Assert.Equal(query, Assert.Single(fixture.Factory.ExecutedQueries));
    }

    [Theory]
    [InlineData("SELECT LEN(ProtectedName) AS Value FROM dbo.T")]
    [InlineData("SELECT 1 AS Value FROM dbo.T WHERE ProtectedName LIKE '%'")]
    [InlineData("SELECT CASE WHEN ProtectedName = 'x' THEN 1 ELSE 0 END AS Value FROM dbo.T")]
    public async Task ExecuteQueryAsync_AllowsFunctionsPredicatesAndCase(string query)
    {
        var fixture = Create(new FakeDbResultSet(["Value"], [[1]]), tokenize: true);

        var result = await fixture.Service.ExecuteQueryAsync(
            TestConstants.QualifiedDatabaseName, query, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value.Data);
        Assert.Equal(1, json.RootElement.GetProperty("Value").GetInt32());
        Assert.Equal(query, Assert.Single(fixture.Factory.ExecutedQueries));
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ExecuteQueryAsync_KeepsCleartextAccessWithoutNotice(AccessLevel accessLevel)
    {
        const string query = "SELECT ProtectedName FROM dbo.T";
        var fixture = Create(new FakeDbResultSet(["ProtectedName"], [["synthetic-secret"]]),
            tokenize: true, accessLevel);

        var result = await fixture.Service.ExecuteQueryAsync(
            TestConstants.QualifiedDatabaseName, query, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.WasAnonymized);
        Assert.Contains("synthetic-secret", result.Value.Data, StringComparison.Ordinal);
        await AssertNoNoticeAsync(fixture.Service, query, result.Value.Data);
    }

    [Theory]
    [InlineData("SELECT 'hello' AS Msg", false)]
    [InlineData("SELECT 'hello' AS Msg FROM dbo.T", false)]
    [InlineData("SELECT 'hello' AS Msg FROM dbo.T WHERE ProtectedName LIKE '%'", true)]
    public async Task ExecuteFileAsync_UsesTheSameColumnReferenceDecision(string query, bool expectedAnonymized)
    {
        var fixture = Create(new FakeDbResultSet(["Msg"], [["hello"]]), tokenize: true);
        var service = new ScriptExecutionService(fixture.Factory, fixture.Validator, fixture.Service,
            Options.Create(fixture.Options), NullLogger<ScriptExecutionService>.Instance);

        var result = await service.ExecuteAsync(new ScriptExecutionRequest(
            new SqlScriptFile("fixture.sql", query, "UTF-8"), TestConstants.QualifiedDatabaseName),
            TestContext.Current.CancellationToken);

        Assert.Equal(ScriptExecutionStatus.Success, result.Status);
        var execution = Assert.Single(Assert.Single(result.Batches).Executions);
        Assert.Equal(expectedAnonymized, execution.WasAnonymized);
        using var json = JsonDocument.Parse(execution.Data);
        Assert.Equal(!expectedAnonymized, json.RootElement.GetProperty("Msg").GetString() == "hello");
    }

    private static async Task AssertNoNoticeAsync(QueryExecutionService service, string query, string data)
    {
        var dispatcher = ToolDispatcherTestHelper.BuildDispatcher(exec: service);
        var result = await dispatcher.DispatchAsync(ToolDispatcherTestHelper.Call(McpConstants.ToolExecuteQuery,
            (McpConstants.ArgDatabase, TestConstants.QualifiedDatabaseName),
            (McpConstants.ArgQuery, query)), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Content.Count);
        Assert.StartsWith("Execution Info:", result.Content[0].Text, StringComparison.Ordinal);
        Assert.Equal(data, result.Content[1].Text);
    }

    private static Fixture Create(FakeDbResultSet resultSet, bool tokenize,
        AccessLevel accessLevel = AccessLevel.ReadOnlyAnonymized, bool bypassGuard = false)
    {
        var options = AnonymizationTestHelper.BuildTokenizationOptions(tokenize);
        var factory = new FixtureFactory(resultSet);
        var validator = bypassGuard
            ? new FakeQuerySafetyValidator(new QuerySafetyCheckResult(accessLevel, accessLevel == AccessLevel.ReadWrite))
            : FakeQuerySafetyValidator.Create(accessLevel: accessLevel);
        var service = new QueryExecutionService(factory, validator,
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), new TokenVault())),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);
        return new Fixture(service, factory, validator, options);
    }

    private sealed record Fixture(QueryExecutionService Service, FixtureFactory Factory,
        IQuerySafetyValidator Validator, SqlToAiOptions Options);

    private sealed class FixtureFactory(FakeDbResultSet resultSet) : IDatabaseConnectionFactory
    {
        public List<string> ExecutedQueries { get; } = [];

        public DbConnection CreateTargetConnection(DatabaseTargetContext target) => new FakeDbConnection(
            connection => new FakeDbCommand(connection, new FakeDbCommandHandlers(
                ExecuteScalar: _ => 1,
                ExecuteReader: command =>
                {
                    ExecutedQueries.Add(command.CommandText);
                    return new FakeDbDataReader([resultSet]);
                })),
            new FakeDbConnectionOptions(BeginTransaction: (connection, _) => new FakeDbTransaction(connection)));

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();
    }
}
