using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class QueryExecutionServiceBrowseColumnsTests
{
    [Fact]
    public async Task ExecuteBatchAsync_ForcedAnonymizationOmitsHiddenCompositeKeyAndRowversion()
    {
        using var reader = new BrowseReader(["X", "KeyA", "KeyB", "Version"], [1, 11, 22, new byte[] { 1, 2 }], [1, 2, 3]);
        var harness = CreateHarness(reader);

        var result = await ExecuteBatchAsync(harness, anonymize: true);

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value.Data);
        Assert.Equal("X", Assert.Single(json.RootElement.EnumerateObject()).Name);
        Assert.Equal(1, json.RootElement.GetProperty("X").GetInt32());
        Assert.Empty(result.Value.AnonymizedColumns);
        Assert.Equal(CommandBehavior.SequentialAccess | CommandBehavior.KeyInfo, harness.Factory.ReaderBehavior);
    }

    [Fact]
    public async Task ExecuteQueryAsync_TokenizesOnlyProjectedProtectedColumn()
    {
        using var reader = new BrowseReader(["ProtectedName", "KeyA", "KeyB", "Version"], ["synthetic-secret", 11, 22, new byte[] { 1, 2 }], [1, 2, 3]);
        var harness = CreateHarness(reader);

        var result = await harness.Service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName,
            "SELECT ProtectedName FROM FictionalRows", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value.Data);
        Assert.Equal("ProtectedName", Assert.Single(json.RootElement.EnumerateObject()).Name);
        string token = json.RootElement.GetProperty("ProtectedName").GetString()!;
        Assert.True(harness.Vault.TryResolve(token, TestTargets.ResolveTarget(harness.Options, TestConstants.QualifiedDatabaseName), out string? original));
        Assert.Equal("synthetic-secret", original);
        Assert.Equal(["FictionalRows.ProtectedName"], result.Value.AnonymizedColumns);
        Assert.Equal(["FictionalRows.ProtectedName"], result.Value.SearchableTokenColumns);
        Assert.Equal(CommandBehavior.SequentialAccess | CommandBehavior.KeyInfo, harness.Factory.ReaderBehavior);
    }

    [Fact]
    public async Task ExecuteQueryAsync_KeepsValueAndOriginOrdinalsAcrossHiddenFields()
    {
        using var reader = new BrowseReader(["ProtectedName", "HiddenSecret", "PublicName", "Version"], ["protected-secret", "hidden-secret", "public-value", new byte[] { 1, 2 }], [1, 3]);
        var rules = new ColumnNameRuleProvider("PublicName");
        var harness = CreateHarness(reader, rules: rules);
        reader.ReverseSchemaRows = true;

        var result = await harness.Service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName,
            "SELECT ProtectedName, PublicName FROM FictionalRows", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value.Data);
        Assert.Equal(["ProtectedName", "PublicName"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("public-value", json.RootElement.GetProperty("PublicName").GetString());
        string token = json.RootElement.GetProperty("ProtectedName").GetString()!;
        Assert.True(harness.Vault.TryResolve(token, TestTargets.ResolveTarget(harness.Options, TestConstants.QualifiedDatabaseName), out string? original));
        Assert.Equal("protected-secret", original);
        Assert.Equal([("dbo", "FictionalRows", "ProtectedName"), ("dbo", "FictionalRows", "PublicName")], rules.Lookups);
        Assert.Equal(["FictionalRows.ProtectedName"], result.Value.AnonymizedColumns);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteQueryAsync_PreservesExplicitlySelectedKeyOrBinaryVersion(bool selectVersion)
    {
        string name = selectVersion ? "Version" : "Id";
        object value = selectVersion ? new byte[] { 0, 1, 127, 255 } : 42;
        using var reader = new BrowseReader([name, "HiddenKey"], [value, 11], [1]);
        var harness = CreateHarness(reader);

        var result = await harness.Service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName,
            $"SELECT {name} FROM FictionalRows", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value.Data);
        var property = Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal(name, property.Name);
        if (selectVersion)
        {
            Assert.Equal(Convert.ToBase64String((byte[])value), property.Value.GetString());
        }
        else
        {
            Assert.Equal(42, property.Value.GetInt32());
        }
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ExecuteQueryAsync_CleartextUsesSequentialAccessAndOmitsHiddenFields(AccessLevel accessLevel)
    {
        using var reader = new BrowseReader(["X", "KeyA", "Version"], [1, 11, new byte[] { 1, 2 }], [1, 2]);
        var harness = CreateHarness(reader, accessLevel);

        var result = await harness.Service.ExecuteQueryAsync(TestConstants.QualifiedDatabaseName,
            "SELECT 1 AS X FROM FictionalRows", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value.Data);
        Assert.Equal("X", Assert.Single(json.RootElement.EnumerateObject()).Name);
        Assert.Equal(CommandBehavior.SequentialAccess, harness.Factory.ReaderBehavior);
    }

    [Theory]
    [InlineData("null", false)]
    [InlineData("null", true)]
    [InlineData("no-hidden", false)]
    [InlineData("no-hidden", true)]
    [InlineData("unsupported", false)]
    [InlineData("unsupported", true)]
    public async Task ExecuteBatchAsync_KeepsAllFieldNamesWhenHiddenMetadataIsUnavailable(string schemaMode, bool anonymize)
    {
        using var reader = new BrowseReader(["X", "KeyA", "Version"], [1, 11, new byte[] { 1, 2 }], [1, 2]) { SchemaMode = schemaMode };
        var harness = CreateHarness(reader);

        var result = await ExecuteBatchAsync(harness, anonymize);

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value.Data);
        Assert.Equal(Enumerable.Range(0, reader.FieldCount).Select(reader.GetName), json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(11, json.RootElement.GetProperty("KeyA").GetInt32());
        Assert.Equal("AQI=", json.RootElement.GetProperty("Version").GetString());
    }

    [Fact]
    public async Task ExecuteBatchAsync_UsesSchemaRowPositionWhenColumnOrdinalIsMissing()
    {
        using var reader = new BrowseReader(["X", "HiddenKey", "Id"], [1, 11, 42], [1]) { SchemaMode = "no-ordinal" };
        var harness = CreateHarness(reader);

        var result = await ExecuteBatchAsync(harness, anonymize: true);

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value.Data);
        Assert.Equal(["X", "Id"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(42, json.RootElement.GetProperty("Id").GetInt32());
    }

    private static async Task<Result<QueryExecutionResult>> ExecuteBatchAsync(Harness harness, bool anonymize)
    {
        await using var connection = harness.Factory.CreateTargetConnection(TestTargets.ResolveTarget(harness.Options, TestConstants.QualifiedDatabaseName));
        var args = new QueryBatchExecutionArgs(connection, null, TestConstants.QualifiedDatabaseName,
            "SELECT 1 AS X FROM FictionalRows", 10, anonymize, null,
            TestTargets.ResolveTarget(harness.Options, TestConstants.QualifiedDatabaseName));
        return await ((IQueryBatchExecutor)harness.Service).ExecuteBatchAsync(args, TestContext.Current.CancellationToken);
    }

    private static Harness CreateHarness(BrowseReader reader, AccessLevel accessLevel = AccessLevel.ReadOnlyAnonymized, IAnonymizationRuleProvider? rules = null)
    {
        var options = AnonymizationTestHelper.BuildTokenizationOptions().WithNamedServers(TestTargets.ServerAlias);
        var vault = new TokenVault();
        var factory = new BrowseFactory(reader);
        var service = new QueryExecutionService(factory,
            new FakeQuerySafetyValidator(new QuerySafetyCheckResult(accessLevel, accessLevel == AccessLevel.ReadWrite)),
            new AnonymizationDependencies(new Anonymizer(Options.Create(options), vault), rules),
            Options.Create(options), NullLogger<QueryExecutionService>.Instance);
        return new Harness(service, factory, options, vault);
    }

    private sealed record Harness(QueryExecutionService Service, BrowseFactory Factory, SqlToAiOptions Options, TokenVault Vault);

    private sealed class BrowseFactory(BrowseReader reader) : IDatabaseConnectionFactory
    {
        public CommandBehavior? ReaderBehavior { get; set; }

        public DbConnection CreateTargetConnection(DatabaseTargetContext target) => new FakeDbConnection(
            connection => new BrowseCommand(connection, reader, this),
            new FakeDbConnectionOptions(BeginTransaction: (connection, _) => new FakeDbTransaction(connection)));

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();
    }

    private sealed class BrowseCommand(DbConnection connection, BrowseReader reader, BrowseFactory factory)
        : FakeDbCommand(connection, new FakeDbCommandHandlers(ExecuteScalar: _ => 1))
    {
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            factory.ReaderBehavior = behavior;
            return reader;
        }
    }

    private sealed class BrowseReader(string[] columns, object?[] values, int[] hiddenOrdinals)
        : FakeDbDataReader(columns, [values])
    {
        public string SchemaMode { get; init; } = "complete";
        public bool ReverseSchemaRows { get; set; }

        public override DataTable? GetSchemaTable()
        {
            if (SchemaMode == "null") return null;
            if (SchemaMode == "unsupported") throw new NotSupportedException();
            var table = new DataTable();
            if (SchemaMode != "no-ordinal") table.Columns.Add("ColumnOrdinal", typeof(int));
            if (SchemaMode != "no-hidden") table.Columns.Add("IsHidden", typeof(bool));
            table.Columns.Add("BaseSchemaName", typeof(string));
            table.Columns.Add("BaseTableName", typeof(string));
            table.Columns.Add("BaseColumnName", typeof(string));
            var ordinals = Enumerable.Range(0, FieldCount);
            foreach (int ordinal in ReverseSchemaRows ? ordinals.Reverse() : ordinals)
            {
                var row = table.NewRow();
                if (SchemaMode != "no-ordinal") row["ColumnOrdinal"] = ordinal;
                if (SchemaMode != "no-hidden") row["IsHidden"] = hiddenOrdinals.Contains(ordinal);
                row["BaseSchemaName"] = "dbo";
                row["BaseTableName"] = "FictionalRows";
                row["BaseColumnName"] = GetName(ordinal);
                table.Rows.Add(row);
            }
            return table;
        }
    }
}
