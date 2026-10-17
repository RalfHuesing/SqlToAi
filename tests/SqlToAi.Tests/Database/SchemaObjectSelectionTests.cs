using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Metadata;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class SchemaObjectSelectionTests
{
    [Theory]
    [InlineData("\" dbo.First \"", "[null,\"\",\"  \",\" Second \",\"dbo.First\"]", new[] { "dbo.First", "Second", "dbo.First" })]
    [InlineData("null", "[\" First \"]", new[] { "First" })]
    [InlineData("\" First \"", "[]", new[] { "First" })]
    [InlineData("\" First \"", "null", new[] { "First" })]
    [InlineData("\" [ dbo ].[ First ] \"", "[]", new[] { "[ dbo ].[ First ]" })]
    public void Normalize_PreservesScalarThenListOrderAndDuplicates(string scalar, string list, string[] expected)
    {
        var result = SchemaObjectSelection.Normalize(Json(scalar), Json(list));
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("42", "[]")]
    [InlineData("true", "[\"valid\"]")]
    [InlineData("{}", "[]")]
    [InlineData("[]", "[]")]
    [InlineData("null", "\"Name\"")]
    [InlineData("null", "{}")]
    [InlineData("null", "42")]
    [InlineData("\"valid\"", "[false]")]
    [InlineData("null", "[\"valid\",{}]")]
    [InlineData("null", "[[]]")]
    public void Normalize_RejectsWrongTypesWithoutCoercion(string scalar, string list)
    {
        var result = SchemaObjectSelection.Normalize(Json(scalar), Json(list));
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Theory]
    [InlineData("null", "null")]
    [InlineData("\" \"", "[]")]
    [InlineData("null", "[null,\"\",\" \"]")]
    public void Normalize_RequiresNamesUnlessExplicitlyAllowedEmpty(string scalar, string list)
    {
        Assert.Equal(SqlToAiError.InvalidParametersCode, SchemaObjectSelection.Normalize(Json(scalar), Json(list)).Error.Code);
        Assert.Empty(SchemaObjectSelection.Normalize(Json(scalar), Json(list), requireNames: false).Value);
        Assert.Equal(SqlToAiError.InvalidParametersCode, SchemaObjectSelection.Normalize().Error.Code);
        Assert.Empty(SchemaObjectSelection.Normalize(requireNames: false).Value);
    }

    [Fact]
    public async Task Resolve_PreservesAssociationsAndSharesExactDuplicateQuotedAliasAndObjectIdWork()
    {
        var factory = new CatalogFactory([new CatalogObject(1, "sales", "Orders")]);
        string[] inputs = ["Orders", "Orders", "[sales].[Orders]", "\"sales\".\"Orders\"", "sales.Orders"];
        var result = await factory.Service().ResolveObjectsAsync(TestConstants.QualifiedDatabaseName, inputs, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(inputs, result.Value.Select(entry => entry.ObjectName));
        Assert.Equal(2, factory.Commands.Count);
        Assert.Equal(1, factory.ConnectionCount);
        var selected = result.Value[0].Object.Value;
        Assert.All(result.Value, entry => Assert.Same(selected, entry.Object.Value));
        Assert.Equal("[sales].[Orders]", selected.Identity.QualifiedName);
        Assert.All(factory.Commands, sql =>
        {
            Assert.Contains("o.name = @ObjectName", sql);
            Assert.Contains("schema_name(o.schema_id) = @SchemaName", sql);
            Assert.DoesNotContain("COLLATE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OBJECT_ID(", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TOP", sql, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData("[odd.schema].[O]]rders]", "odd.schema", "O]rders")]
    [InlineData("\"odd.schema\".\"O\"\"rders\"", "odd.schema", "O\"rders")]
    [InlineData("[A.B]", "dbo", "A.B")]
    [InlineData("[Orders%_]", "dbo", "Orders%_")]
    [InlineData("[Name; SELECT 1]", "dbo", "Name; SELECT 1")]
    public async Task Resolve_ParsesSqlQuotingAndTreatsWildcardsAndSqlAsLiteralNames(string input, string schema, string name)
    {
        var factory = new CatalogFactory([new CatalogObject(5, schema, name)]);
        var result = await factory.Service().ResolveObjectsAsync(TestConstants.QualifiedDatabaseName, [input], true, TestContext.Current.CancellationToken);
        Assert.Equal(name, Assert.Single(result.Value).Object.Value.Identity.ObjectName);
        Assert.Equal(name, Assert.Single(factory.Names));
    }

    [Theory]
    [InlineData("db.dbo.Orders")]
    [InlineData("server.db.dbo.Orders")]
    [InlineData("dbo..Orders")]
    [InlineData("dbo.")]
    [InlineData("[]")]
    [InlineData("[unterminated")]
    [InlineData("Orders; SELECT 1")]
    [InlineData("Orders Extra")]
    [InlineData("Orders -- comment")]
    public async Task Resolve_RejectsInvalidOrCrossDatabaseIdentifiersWithoutCatalogQueries(string input)
    {
        var factory = new CatalogFactory([]);
        var result = await factory.Service().ResolveObjectsAsync(TestConstants.QualifiedDatabaseName, [input], true, TestContext.Current.CancellationToken);
        var entry = Assert.Single(result.Value);
        Assert.Equal(SqlToAiError.InvalidParametersCode, entry.Object.Error.Code);
        Assert.Contains(input, entry.Object.Error.Message);
        Assert.Empty(factory.Commands);
    }

    [Fact]
    public async Task Resolve_ReportsUnknownAmbiguousAndUnsupportedInputsWhilePreservingSuccesses()
    {
        var factory = new CatalogFactory([
            new CatalogObject(1, "dbo", "Orders"), new CatalogObject(2, "sales", "Orders"),
            new CatalogObject(3, "dbo", "CheckOrders", "CHECK_CONSTRAINT")]);
        string[] inputs = ["Orders", "missing", "dbo.CheckOrders", "sales.Orders", "[missing]"];
        var result = await factory.Service().ResolveObjectsAsync(TestConstants.QualifiedDatabaseName, inputs, true, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(inputs, result.Value.Select(entry => entry.ObjectName));
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Value[0].Object.Error.Code);
        Assert.Contains("ambiguous", result.Value[0].Object.Error.Message);
        Assert.Contains("SQL schema", result.Value[0].Object.Error.Message);
        Assert.Equal(SqlToAiError.ObjectNotFound("missing"), result.Value[1].Object.Error);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Value[2].Object.Error.Code);
        Assert.Contains("dbo.CheckOrders", result.Value[2].Object.Error.Message);
        Assert.Equal(2, result.Value[3].Object.Value.Identity.ObjectId);
        Assert.Equal(SqlToAiError.ObjectNotFound("[missing]"), result.Value[4].Object.Error);
        Assert.Equal(4, factory.Commands.Count);
    }

    [Theory]
    [InlineData("USER_TABLE")]
    [InlineData("VIEW")]
    [InlineData("SQL_STORED_PROCEDURE")]
    [InlineData("SQL_SCALAR_FUNCTION")]
    [InlineData("SQL_INLINE_TABLE_VALUED_FUNCTION")]
    [InlineData("SQL_TABLE_VALUED_FUNCTION")]
    public async Task Resolve_PrimarySchemaAcceptsSupportedKinds(string type)
    {
        var factory = new CatalogFactory([new CatalogObject(1, "dbo", "Object", type)]);
        var result = await factory.Service().ResolveObjectsAsync(TestConstants.QualifiedDatabaseName, ["dbo.Object"], false, TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(result.Value).Object.IsSuccess);
    }

    [Theory]
    [InlineData("USER_TABLE", true)]
    [InlineData("VIEW", true)]
    [InlineData("SQL_STORED_PROCEDURE", false)]
    public async Task Resolve_OnlyExportAcceptsTableAndViewTriggers(string parentType, bool supportedExport)
    {
        var factory = new CatalogFactory([new CatalogObject(1, "sales", "Changed", "SQL_TRIGGER", parentType)]);
        var primary = await factory.Service().ResolveObjectsAsync(TestConstants.QualifiedDatabaseName, ["Changed"], false, TestContext.Current.CancellationToken);
        var export = await factory.Service().ResolveObjectsAsync(TestConstants.QualifiedDatabaseName, ["Changed"], true, TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.InvalidParametersCode, Assert.Single(primary.Value).Object.Error.Code);
        Assert.Equal(supportedExport, Assert.Single(export.Value).Object.IsSuccess);
        if (supportedExport) Assert.Equal("sales.Parent", Assert.Single(export.Value).Object.Value.Parent!.DisplayName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resolve_UsesDatabaseMatchesWithoutCaseInsensitiveDeduplication(bool caseSensitive)
    {
        var factory = new CatalogFactory([
            new CatalogObject(1, "dbo", "Orders"),
            new CatalogObject(2, "DBO", "orders")], caseSensitive);
        var result = await factory.Service().ResolveObjectsAsync(TestConstants.QualifiedDatabaseName, ["dbo.Orders", "DBO.orders"], false, TestContext.Current.CancellationToken);
        Assert.Equal(2, factory.Commands.Count);
        if (caseSensitive)
        {
            Assert.Equal(1, result.Value[0].Object.Value.Identity.ObjectId);
            Assert.Equal(2, result.Value[1].Object.Value.Identity.ObjectId);
        }
        else
        {
            Assert.All(result.Value, entry => Assert.Equal(SqlToAiError.InvalidParametersCode, entry.Object.Error.Code));
        }
    }

    [Fact]
    public async Task Resolve_CaseInsensitiveDatabaseAliasesShareObjectIdentityAfterSeparateLookups()
    {
        var factory = new CatalogFactory([new CatalogObject(1, "dbo", "Orders")], false);
        var result = await factory.Service().ResolveObjectsAsync(TestConstants.QualifiedDatabaseName, ["dbo.Orders", "DBO.orders"], false, TestContext.Current.CancellationToken);
        Assert.Equal(2, factory.Commands.Count);
        Assert.Same(result.Value[0].Object.Value, result.Value[1].Object.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_DeniedDatabaseNeverOpensConnectionOrExposesCatalog(bool excluded)
    {
        var factory = new CatalogFactory([new CatalogObject(1, "dbo", "Orders")]);
        var result = await factory.Service(allowed: excluded, excluded: excluded)
            .ResolveObjectsAsync(TestConstants.QualifiedDatabaseName, ["Orders"], false, TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Equal(0, factory.ConnectionCount);
    }

    [Fact]
    public async Task Resolve_PreservesRequestWideQueryFailures()
    {
        var factory = new CatalogFactory([]) { FailQuery = true };
        var result = await factory.Service().ResolveObjectsAsync(TestConstants.QualifiedDatabaseName, ["Orders"], false, TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.QueryError("fixed selection failure"), result.Error);
    }

    [Fact]
    public async Task Resolve_PropagatesCancellationWithoutCatalogExposure()
    {
        var factory = new CatalogFactory([]);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.Service()
            .ResolveObjectsAsync(TestConstants.QualifiedDatabaseName, ["Orders"], false, cancelled.Token));
        Assert.Empty(factory.Commands);
    }

    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);

    internal sealed record CatalogObject(int Id, string Schema, string Name, string Type = "USER_TABLE", string? ParentType = null);

    internal sealed class CatalogFactory(IReadOnlyList<CatalogObject> objects, bool caseSensitive = true) : IDatabaseConnectionFactory
    {
        public DbConnection CreateTargetConnection(SqlToAi.Domain.DatabaseTargetContext target) => CreateConnection(target.DatabaseName);
        public SqlToAi.Domain.Result<System.Data.Common.DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

        public List<string> Commands { get; } = [];
        public List<string> Names { get; } = [];
        public int ConnectionCount { get; private set; }
        public bool FailQuery { get; init; }

        public SchemaService Service(bool allowed = true, bool excluded = false)
        {
            var options = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
            if (allowed) options.Databases.SchemaOnly = [TestConstants.QualifiedDatabaseName];
            if (excluded) options.Servers[TestTargets.ServerAlias].ExcludedDatabases = ["DemoDB"];
            var bound = Options.Create(options);
            return new SchemaService(this, new SecurityGuard(bound),
                new AccessLevelProvider(this, bound, NullLogger<AccessLevelProvider>.Instance),
                new MetadataProvider(this, bound, NullLogger<MetadataProvider>.Instance),
                new AlwaysAllowPolicyResolver(), bound, NullLogger<SchemaService>.Instance);
        }

        public DbConnection CreateConnection(string? databaseName = null)
        {
            ConnectionCount++;
            return new FakeDbConnection(connection => new FakeDbCommand(connection, new FakeDbCommandHandlers(ExecuteReader: Read)));
        }

        private FakeDbDataReader Read(FakeDbCommand command)
        {
            Commands.Add(command.CommandText);
            if (FailQuery) throw new InvalidOperationException("fixed selection failure");
            var parameters = command.Parameters.Cast<DbParameter>().ToDictionary(parameter => parameter.ParameterName, parameter => parameter.Value, StringComparer.Ordinal);
            var name = (string)parameters["ObjectName"]!;
            var schema = parameters["SchemaName"] as string;
            Names.Add(name);
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return new FakeDbDataReader(["ObjectId", "SchemaName", "ObjectName", "TypeDescription", "ParentObjectId", "ParentSchemaName", "ParentObjectName", "ParentTypeDescription"],
                objects.Where(item => string.Equals(item.Name, name, comparison) && (schema is null || string.Equals(item.Schema, schema, comparison)))
                    .Select(item => new object?[] { item.Id, item.Schema, item.Name, item.Type,
                        item.ParentType is null ? null : 123, item.ParentType is null ? null : item.Schema,
                        item.ParentType is null ? null : "Parent", item.ParentType }).ToArray());
        }
    }
}
