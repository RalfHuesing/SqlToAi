using System.Text.Json;
using SqlToAi.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Database;
using SqlToAi.Metadata;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class SchemaServiceBatchTests
{
    [Fact]
    public async Task Batch_NormalizesInputsRetainsOrderAndReusesResolvedAliasesWithInteractiveEnrichment()
    {
        var factory = new SchemaBatchTestFactory();
        var response = await factory.Service().GetSchemaBatchAsync(TestConstants.QualifiedDatabaseName, Json("\" Orders \""),
            Json("[null,\"\",\"  \",\"[dbo].[Orders]\",\"dbo.Orders\",\"Orders\"]"), TestContext.Current.CancellationToken);
        Assert.True(response.Value.Complete);
        var results = Results(response.Value.Json);
        Assert.Equal(["Orders", "[dbo].[Orders]", "dbo.Orders", "Orders"], results.Select(item => item.GetProperty("object_name").GetString()));
        Assert.Single(factory.SchemaFetches);
        Assert.Equal("dbo.Orders", Assert.Single(factory.Metadata.Calls));
        Assert.Equal("[dbo].[Orders]", factory.SchemaFetches[0]);
        string markdown = results[0].GetProperty("markdown").GetString()!;
        Assert.All(results, item => Assert.Equal(markdown, item.GetProperty("markdown").GetString()));
        Assert.Contains("enriched table description", markdown);
        Assert.Contains("| Secret | varchar(50) | Yes |  | Yes (searchable) | enriched column description |", markdown);
        Assert.Contains("| Exempt | varchar(50) | Yes |  | No |  |", markdown);
        Assert.Contains("| Id | int | No |  | No |  |", markdown);
        Assert.Contains("Discovery Index", markdown);
        Assert.Contains("Changed", markdown);
        Assert.DoesNotContain(factory.Commands, sql => sql.Contains("SELECT RTRIM(type)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[dbo].[Orders]", "Schema for Table/View")]
    [InlineData("[dbo].[View]", "View Definition")]
    [InlineData("[dbo].[Procedure]", "sql_get_routine_parameters")]
    [InlineData("[dbo].[Scalar]", "sql_get_routine_parameters")]
    [InlineData("[dbo].[Inline]", "sql_get_routine_parameters")]
    [InlineData("[dbo].[Function]", "sql_get_routine_parameters")]
    public async Task Batch_RendersPrimarySupportedKinds(string name, string expected)
    {
        var factory = new SchemaBatchTestFactory();
        var result = await factory.Service().GetSchemaBatchAsync(TestConstants.QualifiedDatabaseName, objectNames: Json(JsonSerializer.Serialize(new[] { name })),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Value.Complete);
        Assert.Contains(expected, Assert.Single(Results(result.Value.Json)).GetProperty("markdown").GetString());
        Assert.DoesNotContain(factory.Commands, sql => sql.Contains("sys.foreign_key_columns", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Batch_ContinuesAfterMissingAmbiguousUnsupportedAndFetchFailures()
    {
        var factory = new SchemaBatchTestFactory { FailingSchema = "[dbo].[Broken]" };
        var result = await factory.Service().GetSchemaBatchAsync(TestConstants.QualifiedDatabaseName, objectNames: Json("[\"missing\",\"Ambiguous\",\"Unsupported\",\"Broken\",\"Orders\"]"),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Complete);
        var results = Results(result.Value.Json);
        Assert.Equal(["error", "error", "error", "error", "ok"], results.Select(item => item.GetProperty("status").GetString()));
        Assert.Equal(SqlToAiError.ObjectNotFoundCode, results[0].GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("ambiguous", results[1].GetProperty("error").GetProperty("message").GetString());
        Assert.Contains("supported", results[2].GetProperty("error").GetProperty("message").GetString());
        Assert.Contains("fixed schema failure", results[3].GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Batch_CountsNormalizedInputsBeforeDeduplicationAndBeforeConnections()
    {
        var factory = new SchemaBatchTestFactory();
        factory.Options.SchemaBatch.MaxInputCount = 2;
        var result = await factory.Service().GetSchemaBatchAsync(TestConstants.QualifiedDatabaseName, Json("\"Orders\""), Json("[\"Orders\",null,\" \",\"Orders\"]"),
            TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Equal(0, factory.ConnectionCount);
        var atLimit = await factory.Service().GetSchemaBatchAsync(TestConstants.QualifiedDatabaseName, Json("\"Orders\""), Json("[null,\" \",\"Orders\"]"),
            TestContext.Current.CancellationToken);
        Assert.True(atLimit.Value.Complete);
        Assert.Equal(2, Results(atLimit.Value.Json).Length);
        Assert.Single(factory.SchemaFetches);
    }

    [Theory]
    [InlineData("\"Orders\"", "[7]")]
    [InlineData("42", "[\"Orders\"]")]
    [InlineData("null", "[null,\"\",\"  \"]")]
    [InlineData("null", "\"Orders\"")]
    public async Task Batch_InvalidInputsFailBeforeDatabaseWork(string scalar, string list)
    {
        var factory = new SchemaBatchTestFactory();
        var result = await factory.Service().GetSchemaBatchAsync(TestConstants.QualifiedDatabaseName, Json(scalar), Json(list), TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Equal(0, factory.ConnectionCount);
    }

    [Fact]
    public async Task Batch_EscapedMetadataPreflightRejectsBeforeCatalogOrSchemaWork()
    {
        var factory = new SchemaBatchTestFactory();
        factory.Options.SchemaBatch.MaxResponseBytes = 200;
        var result = await factory.Service().GetSchemaBatchAsync(TestConstants.QualifiedDatabaseName, Json(JsonSerializer.Serialize(new string('名', 100))),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Equal(0, factory.ConnectionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batch_DatabaseDenialFailsWholeCallBeforeCatalogExposure(bool excluded)
    {
        var factory = new SchemaBatchTestFactory();
        if (excluded) factory.Options.Servers[TestTargets.ServerAlias].ExcludedDatabases = ["DemoDB"];
        else factory.Options.Databases.SchemaOnly.Clear();
        var result = await factory.Service().GetSchemaBatchAsync(TestConstants.QualifiedDatabaseName, Json("\"Orders\""), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Equal(0, factory.ConnectionCount);
    }

    [Fact]
    public async Task Batch_CatalogFailureFailsWholeCallWithoutSchemas()
    {
        var factory = new SchemaBatchTestFactory { FailCatalog = true };
        var result = await factory.Service().GetSchemaBatchAsync(TestConstants.QualifiedDatabaseName, Json("\"Orders\""), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.QueryError("fixed catalog failure"), result.Error);
        Assert.Empty(factory.SchemaFetches);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Batch_RequiresDatabaseBeforeOpeningConnection(string database)
    {
        var factory = new SchemaBatchTestFactory();
        var result = await factory.Service().GetSchemaBatchAsync(database, Json("\"Orders\""), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Equal(0, factory.ConnectionCount);
    }

    [Fact]
    public async Task Batch_ConnectionTimeoutPreservesRequestWideCatalogError()
    {
        var factory = new SchemaBatchTestFactory { OpenFailure = new TimeoutException("sensitive provider details") };
        var result = await factory.Service().GetSchemaBatchAsync(TestConstants.QualifiedDatabaseName, Json("\"Orders\""), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.TimeoutCode, result.Error.Code);
        Assert.DoesNotContain("sensitive", result.Error.Message);
        Assert.Empty(factory.Commands);
    }

    [Fact]
    public async Task Batch_CancelledRequestDoesNotOpenConnection()
    {
        var factory = new SchemaBatchTestFactory();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.Service().GetSchemaBatchAsync(TestConstants.QualifiedDatabaseName, Json("\"Orders\""), cancellationToken: cancelled.Token));
        Assert.Equal(0, factory.ConnectionCount);
    }

    [Fact]
    public async Task Scalar_DoesNotApplyBatchLimitsAndRetainsMarkdown()
    {
        var factory = new SchemaBatchTestFactory();
        factory.Options.SchemaBatch.MaxResponseBytes = 1;
        var result = await factory.Service().GetSchemaAsync(TestConstants.QualifiedDatabaseName, "dbo.Orders", TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.StartsWith("# Schema for Table/View: `dbo.Orders`", result.Value);
        Assert.Contains("Discovery Index", result.Value);
    }

    [Theory]
    [InlineData("dbo", "Orders", true)]
    [InlineData("dbo", "Order]s.Detail", true)]
    [InlineData("s.ch]ema", "Order]s.Detail", true)]
    [InlineData("s.ch]ema", "Order]s.Detail", false)]
    public async Task Batch_UsesResolvedNamesForNativeCustomMetadataAndSchemaSpecificUnmaskedRules(string schema, string name, bool custom)
    {
        var factory = new SchemaBatchTestFactory { CatalogSchema = schema };
        if (custom)
        {
            factory.Options.Servers[TestTargets.ServerAlias].MetadataProvider.TableMetadataQuery = "CUSTOM_TABLE_DESCRIPTION";
            factory.Options.Servers[TestTargets.ServerAlias].MetadataProvider.ColumnMetadataQuery = "CUSTOM_COLUMN_DESCRIPTION";
        }
        factory.Options.Anonymizer.Tokenization.Enabled = true;
        var bound = Options.Create(factory.Options);
        var identity = new SchemaObjectIdentity(1, schema, name, SchemaObjectKind.Table);
        var rule = new ExactUnmaskedRule(schema, name);
        var metadata = new MetadataProvider(factory, bound, NullLogger<MetadataProvider>.Instance);
        var policy = new AnonymizationPolicyResolver(bound, rule);
        var response = await factory.Service(metadata, policy).GetSchemaBatchAsync(TestConstants.QualifiedDatabaseName,
            Json(JsonSerializer.Serialize(identity.QualifiedName)), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(response.Value.Complete);
        string markdown = Assert.Single(Results(response.Value.Json)).GetProperty("markdown").GetString()!;
        Assert.Contains("custom table description", markdown);
        Assert.Contains("custom column description", markdown);
        Assert.Contains("| Secret | varchar(50) | Yes |  | Yes (searchable)", markdown);
        Assert.Contains("| Exempt | varchar(50) | Yes |  | No |", markdown);
        Assert.Equal([custom ? name : identity.QualifiedName, custom ? name : identity.QualifiedName], factory.MetadataQueryNames);
        Assert.Equal(["Secret", "Exempt"], rule.Columns);
    }

    [Fact]
    public async Task Batch_CustomMetadataMatchesScalarForOrdinaryNames()
    {
        var factory = new SchemaBatchTestFactory();
        factory.Options.Servers[TestTargets.ServerAlias].MetadataProvider.TableMetadataQuery = "CUSTOM_TABLE_DESCRIPTION";
        factory.Options.Servers[TestTargets.ServerAlias].MetadataProvider.ColumnMetadataQuery = "CUSTOM_COLUMN_DESCRIPTION";
        var metadata = new MetadataProvider(factory, Options.Create(factory.Options), NullLogger<MetadataProvider>.Instance);
        var service = factory.Service(metadata);
        var scalar = await service.GetSchemaAsync(TestConstants.QualifiedDatabaseName, "dbo.Orders", TestContext.Current.CancellationToken);
        var batch = await service.GetSchemaBatchAsync(TestConstants.QualifiedDatabaseName, Json("\"[dbo].[Orders]\""), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(scalar.IsSuccess);
        Assert.True(batch.Value.Complete);
        string markdown = Assert.Single(Results(batch.Value.Json)).GetProperty("markdown").GetString()!;
        Assert.Contains("custom table description", scalar.Value);
        Assert.Contains("custom table description", markdown);
        Assert.Contains("custom column description", scalar.Value);
        Assert.Contains("custom column description", markdown);
        Assert.Equal(["Orders", "Orders", "Orders", "Orders"], factory.MetadataQueryNames);
    }

    private sealed class ExactUnmaskedRule(string expectedSchema, string expectedTable) : IAnonymizationRuleProvider
    {
        internal List<string> Columns { get; } = [];

        public Task<bool> IsExcludedAsync(SqlToAi.Domain.DatabaseTargetContext target, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
        {
            Assert.Equal(TestConstants.DatabaseName, target.DatabaseName);
            Assert.Equal(TestTargets.ServerAlias, target.ServerAlias);
            Assert.Equal(expectedSchema, schemaName);
            Assert.Equal(expectedTable, tableName);
            Columns.Add(columnName);
            return Task.FromResult(columnName == "Exempt");
        }
    }

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);
    private static JsonElement[] Results(string json) => Json(json).GetProperty("results").EnumerateArray().ToArray();
}
