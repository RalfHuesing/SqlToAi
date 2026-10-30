using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using SqlToAi.Configuration;
using SqlToAi.Mcp;

namespace SqlToAi.Tests.Mcp;

/// <summary>
/// Unit tests for JSON-RPC 2.0 and MCP message model serialization/deserialization.
/// </summary>
public sealed class McpModelsTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    // -------------------------------------------------------------------------
    // ToolDefinition
    // -------------------------------------------------------------------------

    [Fact]
    public void ToolDefinition_ShouldSerialize_WithRequiredProperties()
    {
        var tool = new ToolDefinition
        {
            Name = McpConstants.ToolGetSchema,
            Description = "Returns schema for a database object.",
            InputSchema = new ToolInputSchema
            {
                Properties = new Dictionary<string, ToolParameterDefinition>
                {
                    [McpConstants.ArgObjectName] = new() { Type = "string", Description = "Object name." },
                    [McpConstants.ArgDatabase]   = new() { Type = "string", Description = "Database name." }
                },
                Required = [McpConstants.ArgObjectName]
            }
        };

        string json = JsonSerializer.Serialize(tool, Options);

        Assert.Contains("sql_get_schema", json);
        Assert.Contains("object_name", json);
        Assert.Contains("required", json);
    }

    // -------------------------------------------------------------------------
    // ToolCallResult helpers
    // -------------------------------------------------------------------------

    [Fact]
    public void ToolCallResult_Success_ShouldNotBeError()
    {
        var result = ToolCallResult.Success("# Schema\nSome content");
        Assert.False(result.IsError);
        Assert.Single(result.Content);
        Assert.Equal("text", result.Content[0].Type);
        Assert.Contains("# Schema", result.Content[0].Text);
    }

    [Fact]
    public void ToolCallResult_Failure_ShouldBeError_AndContainCode()
    {
        var result = ToolCallResult.Failure("SQL-AI-0104", "Safety check failed.");
        Assert.True(result.IsError);
        Assert.Single(result.Content);
        Assert.Contains("SQL-AI-0104", result.Content[0].Text);
    }

    // -------------------------------------------------------------------------
    // Server options
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ServerOptions_ShouldAdvertiseServerInfo()
    {
        using var temp = TestTempDirectory.Create();
        var configuration = CreateValidConfiguration();
        await using var services = Program.BuildServiceProvider(
            configuration, configuration.GetSection("SqlToAi").Get<SqlToAiOptions>()!, temp.DirectoryPath);
        var options = services.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.ServerInfo);
        Assert.Equal(McpConstants.ServerName, options.ServerInfo.Name);
        Assert.Equal(typeof(Program).Assembly.GetName().Version?.ToString() ?? McpConstants.ServerVersion,
            options.ServerInfo.Version);
    }

    [Fact]
    public async Task ServerOptions_ShouldAdvertiseAnonymizationInstructions()
    {
        using var temp = TestTempDirectory.Create();
        var configuration = CreateValidConfiguration();
        await using var services = Program.BuildServiceProvider(
            configuration, configuration.GetSection("SqlToAi").Get<SqlToAiOptions>()!, temp.DirectoryPath);
        var options = services.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(McpConstants.ServerInstructions, options.ServerInstructions);
        Assert.Contains("Anonymized", options.ServerInstructions, StringComparison.Ordinal);
        Assert.Contains("sql_get_object_references", options.ServerInstructions, StringComparison.Ordinal);
    }

    private static IConfigurationRoot CreateValidConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SqlToAi:Servers:primary:Server"] = "test-host"
        }).Build();

    // -------------------------------------------------------------------------
    // McpConstants completeness
    // -------------------------------------------------------------------------

    [Fact]
    public void McpConstants_ToolNames_ShouldMatchSpecification()
    {
        // All current tools must be present
        var tools = new[]
        {
            McpConstants.ToolListServers,
            McpConstants.ToolListDatabases,
            McpConstants.ToolSearchDatabases,
            McpConstants.ToolGetEnvironment,
            McpConstants.ToolValidateQuery,
            McpConstants.ToolSearchObjects,
            McpConstants.ToolGetSchema,
            McpConstants.ToolExportSchema,
            McpConstants.ToolGetSchemaForeignKeys,
            McpConstants.ToolGetSchemaIndexes,
            McpConstants.ToolGetSchemaConstraints,
            McpConstants.ToolGetTriggerDefinition,
            McpConstants.ToolGetObjectReferences,
            McpConstants.ToolGetRoutineParameters,
            McpConstants.ToolExecuteQuery,
            McpConstants.ToolExportData,
            McpConstants.ToolExecuteFile,
            McpConstants.ToolCompareQueries,
            McpConstants.ToolMeasurePerformance,
            McpConstants.ToolBenchmarkOptimization,
            McpConstants.ToolSuggestIndexes
        };

        Assert.Equal(21, tools.Distinct().Count());
        Assert.All(tools, t => Assert.StartsWith("sql_", t));
    }
}
