#nullable enable

using System.Globalization;
using System.Text.Json;
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
    // JsonRpcRequest
    // -------------------------------------------------------------------------

    [Fact]
    public void JsonRpcRequest_ShouldDeserialize_WithMethodAndParams()
    {
        const string json = """
            {
              "jsonrpc": "2.0",
              "id": 1,
              "method": "tools/list",
              "params": {}
            }
            """;

        var request = JsonSerializer.Deserialize<JsonRpcRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Equal("2.0", request.JsonRpc);
        Assert.Equal("tools/list", request.Method);
    }

    [Fact]
    public void JsonRpcRequest_ShouldDeserialize_WithoutId_AsNotification()
    {
        const string json = """
            {
              "jsonrpc": "2.0",
              "method": "notifications/initialized"
            }
            """;

        var request = JsonSerializer.Deserialize<JsonRpcRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Null(request.Id);
        Assert.Equal("notifications/initialized", request.Method);
    }

    // -------------------------------------------------------------------------
    // JsonRpcResponse
    // -------------------------------------------------------------------------

    [Fact]
    public void JsonRpcResponse_ShouldSerialize_WithResultPayload()
    {
        var response = new JsonRpcResponse
        {
            Id = JsonDocument.Parse("1").RootElement,
            Result = new { value = "hello" }
        };

        string json = JsonSerializer.Serialize(response, Options);

        Assert.Contains("\"jsonrpc\":\"2.0\"", json);
        Assert.Contains("\"result\"", json);
    }

    // -------------------------------------------------------------------------
    // JsonRpcErrorResponse
    // -------------------------------------------------------------------------

    [Fact]
    public void JsonRpcErrorResponse_ShouldCarryErrorCodeAndMessage()
    {
        var error = new JsonRpcErrorResponse
        {
            Id = JsonDocument.Parse("42").RootElement,
            Error = new JsonRpcError
            {
                Code = JsonRpcError.MethodNotFound,
                Message = "Method not found"
            }
        };

        string json = JsonSerializer.Serialize(error, Options);

        Assert.Contains(JsonRpcError.MethodNotFound.ToString(CultureInfo.InvariantCulture), json);
        Assert.Contains("Method not found", json);
    }

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
    // InitializeResult
    // -------------------------------------------------------------------------

    [Fact]
    public void InitializeResult_ShouldAdvertiseServerInfo()
    {
        var initResult = new InitializeResult();
        Assert.Equal(McpConstants.ProtocolVersion, initResult.ProtocolVersion);
        Assert.Equal(McpConstants.ServerName, initResult.ServerInfo.Name);
        Assert.Equal(McpConstants.ServerVersion, initResult.ServerInfo.Version);
    }

    [Fact]
    public void InitializeResult_ShouldAdvertiseAnonymizationInstructions()
    {
        var initResult = new InitializeResult();
        Assert.Contains("Anonymized", initResult.Instructions, StringComparison.Ordinal);
        Assert.Contains("sql_get_object_references", initResult.Instructions, StringComparison.Ordinal);

        string json = JsonSerializer.Serialize(initResult, Options);
        Assert.Contains("\"instructions\"", json);
    }

    // -------------------------------------------------------------------------
    // McpConstants completeness
    // -------------------------------------------------------------------------

    [Fact]
    public void McpConstants_ToolNames_ShouldMatchSpecification()
    {
        // All 16 tools defined in architecture-spec.md must be present
        var tools = new[]
        {
            McpConstants.ToolListDatabases,
            McpConstants.ToolSearchDatabases,
            McpConstants.ToolValidateQuery,
            McpConstants.ToolSearchObjects,
            McpConstants.ToolGetSchema,
            McpConstants.ToolGetSchemaForeignKeys,
            McpConstants.ToolGetSchemaIndexes,
            McpConstants.ToolGetSchemaConstraints,
            McpConstants.ToolGetTriggerDefinition,
            McpConstants.ToolGetObjectReferences,
            McpConstants.ToolGetRoutineParameters,
            McpConstants.ToolExecuteQuery,
            McpConstants.ToolCompareQueries,
            McpConstants.ToolMeasurePerformance,
            McpConstants.ToolBenchmarkOptimization,
            McpConstants.ToolSuggestIndexes
        };

        Assert.Equal(16, tools.Distinct().Count());
        Assert.All(tools, t => Assert.StartsWith("sql_", t));
    }
}
