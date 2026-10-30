using SqlToAi.Mcp;

namespace SqlToAi.Tests.Mcp;

/// <summary>
/// Unit tests for <see cref="ToolRegistry"/> — validates that all 22 tools are correctly registered
/// and that their schema definitions are consistent with <see cref="McpConstants"/>.
/// </summary>
public sealed class ToolRegistryTests
{
    private readonly ToolRegistry _registry = new();

    [Fact]
    public void GetAll_ShouldReturn_TwentyTwoTools()
    {
        Assert.Equal(22, _registry.GetAll().Count);
    }

    [Fact]
    public void GetAll_ShouldContainAllToolNames()
    {
        var names = _registry.GetAll().Select(t => t.Name).ToHashSet();

        Assert.Contains(McpConstants.ToolListServers, names);
        Assert.Contains(McpConstants.ToolListDatabases, names);
        Assert.Contains(McpConstants.ToolSearchDatabases, names);
        Assert.Contains(McpConstants.ToolGetEnvironment, names);
        Assert.Contains(McpConstants.ToolValidateQuery, names);
        Assert.Contains(McpConstants.ToolExplainQuery, names);
        Assert.Contains(McpConstants.ToolSearchObjects, names);
        Assert.Contains(McpConstants.ToolGetSchema, names);
        Assert.Contains(McpConstants.ToolExportSchema, names);
        Assert.Contains(McpConstants.ToolCompareQueries, names);
        Assert.Contains(McpConstants.ToolMeasurePerformance, names);
        Assert.Contains(McpConstants.ToolBenchmarkOptimization, names);
        Assert.Contains(McpConstants.ToolGetSchemaForeignKeys, names);
        Assert.Contains(McpConstants.ToolGetSchemaIndexes, names);
        Assert.Contains(McpConstants.ToolGetSchemaConstraints, names);
        Assert.Contains(McpConstants.ToolGetTriggerDefinition, names);
        Assert.Contains(McpConstants.ToolGetObjectReferences, names);
        Assert.Contains(McpConstants.ToolGetRoutineParameters, names);
        Assert.Contains(McpConstants.ToolExecuteQuery, names);
        Assert.Contains(McpConstants.ToolExportData, names);
        Assert.Contains(McpConstants.ToolExecuteFile, names);
        Assert.Contains(McpConstants.ToolSuggestIndexes, names);
    }

    [Fact]
    public void AllTools_ShouldHaveNonEmptyDescription()
    {
        Assert.All(_registry.GetAll(), t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
    }

    [Fact]
    public void AllTools_ShouldHaveObjectSchema()
    {
        Assert.All(_registry.GetAll(), t => Assert.Equal("object", t.InputSchema.Type));
    }

    [Theory]
    [InlineData(McpConstants.ToolListDatabases, McpConstants.ArgServer)]
    [InlineData(McpConstants.ToolSearchDatabases, McpConstants.ArgServer)]
    [InlineData(McpConstants.ToolSearchDatabases, McpConstants.ArgSearchTerm)]
    [InlineData(McpConstants.ToolValidateQuery, McpConstants.ArgQuery)]
    [InlineData(McpConstants.ToolSearchObjects, McpConstants.ArgSearchTerm)]
    [InlineData(McpConstants.ToolGetSchemaForeignKeys, McpConstants.ArgObjectName)]
    [InlineData(McpConstants.ToolGetSchemaIndexes, McpConstants.ArgObjectName)]
    [InlineData(McpConstants.ToolGetSchemaConstraints, McpConstants.ArgObjectName)]
    [InlineData(McpConstants.ToolGetTriggerDefinition, McpConstants.ArgObjectName)]
    [InlineData(McpConstants.ToolGetObjectReferences, McpConstants.ArgObjectName)]
    [InlineData(McpConstants.ToolGetRoutineParameters, McpConstants.ArgObjectName)]
    [InlineData(McpConstants.ToolExecuteQuery, McpConstants.ArgQuery)]
    [InlineData(McpConstants.ToolExportData, McpConstants.ArgDatabase)]
    [InlineData(McpConstants.ToolExportData, McpConstants.ArgQuery)]
    [InlineData(McpConstants.ToolExportData, McpConstants.ArgOutputPath)]
    [InlineData(McpConstants.ToolExecuteFile, McpConstants.ArgFilePath)]
    [InlineData(McpConstants.ToolGetEnvironment, McpConstants.ArgDatabase)]
    [InlineData(McpConstants.ToolExplainQuery, McpConstants.ArgDatabase)]
    [InlineData(McpConstants.ToolExplainQuery, McpConstants.ArgQuery)]
    public void Tool_ShouldHaveRequiredArgument(string toolName, string expectedRequired)
    {
        var tool = _registry.GetAll().Single(t => t.Name == toolName);
        Assert.Contains(expectedRequired, tool.InputSchema.Required);
    }

    [Fact]
    public void GetTriggerDefinition_ShouldRequireBoth_ObjectName_And_TriggerName()
    {
        var tool = _registry.GetAll().Single(t => t.Name == McpConstants.ToolGetTriggerDefinition);
        Assert.Contains(McpConstants.ArgObjectName, tool.InputSchema.Required);
        Assert.Contains(McpConstants.ArgTriggerName, tool.InputSchema.Required);
    }

    [Fact]
    public void ListServers_ShouldHaveNoRequiredArgs()
    {
        var tool = _registry.GetAll().Single(t => t.Name == McpConstants.ToolListServers);
        Assert.Empty(tool.InputSchema.Required);
    }

    [Fact]
    public void ListDatabases_ShouldRequireServer()
    {
        var tool = _registry.GetAll().Single(t => t.Name == McpConstants.ToolListDatabases);
        Assert.Contains(McpConstants.ArgServer, tool.InputSchema.Required);
    }

    [Fact]
    public void GetEnvironment_ShouldExposeExpectedArgumentSchema()
    {
        var tool = _registry.GetAll().Single(t => t.Name == McpConstants.ToolGetEnvironment);
        Assert.Contains(McpConstants.ArgDatabase, tool.InputSchema.Required);
        Assert.Single(tool.InputSchema.Required);
        Assert.Equal("string", tool.InputSchema.Properties[McpConstants.ArgDatabase].Type);
        Assert.Equal(McpConstants.DatabaseTargetDescription, tool.InputSchema.Properties[McpConstants.ArgDatabase].Description);
    }

    [Fact]
    public void ExplainQuery_ShouldExposeExpectedArgumentSchema()
    {
        var tool = _registry.GetAll().Single(t => t.Name == McpConstants.ToolExplainQuery);

        Assert.Contains(McpConstants.ArgDatabase, tool.InputSchema.Required);
        Assert.Contains(McpConstants.ArgQuery, tool.InputSchema.Required);
        Assert.Equal("string", tool.InputSchema.Properties[McpConstants.ArgDatabase].Type);
        Assert.Equal("string", tool.InputSchema.Properties[McpConstants.ArgQuery].Type);
        Assert.Equal("object", tool.InputSchema.Properties[McpConstants.ArgParameters].Type);
        Assert.Equal(McpConstants.DatabaseTargetDescription, tool.InputSchema.Properties[McpConstants.ArgDatabase].Description);
    }

    [Fact]
    public void ExecuteQuery_ShouldExposeRequestedRowLimit_AsInteger()
    {
        var tool = _registry.GetAll().Single(t => t.Name == McpConstants.ToolExecuteQuery);
        Assert.True(tool.InputSchema.Properties.TryGetValue(McpConstants.ArgRequestedRowLimit, out var param));
        Assert.Equal("integer", param.Type);
    }

    [Fact]
    public void ExecuteFile_ShouldExposeExpectedArgumentSchema()
    {
        var tool = _registry.GetAll().Single(t => t.Name == McpConstants.ToolExecuteFile);

        Assert.Contains(McpConstants.ArgFilePath, tool.InputSchema.Required);
        Assert.Contains(McpConstants.ArgDatabase, tool.InputSchema.Required);
        Assert.Equal("string", tool.InputSchema.Properties[McpConstants.ArgFilePath].Type);
        Assert.Equal("string", tool.InputSchema.Properties[McpConstants.ArgDatabase].Type);
        Assert.Equal("boolean", tool.InputSchema.Properties[McpConstants.ArgUseTransaction].Type);
        Assert.Equal("integer", tool.InputSchema.Properties[McpConstants.ArgRequestedRowLimit].Type);
        Assert.Equal("object", tool.InputSchema.Properties[McpConstants.ArgParameters].Type);
        Assert.Contains("true", tool.InputSchema.Properties[McpConstants.ArgUseTransaction].Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportData_ShouldExposeExpectedArgumentSchema()
    {
        var tool = _registry.GetAll().Single(t => t.Name == McpConstants.ToolExportData);

        Assert.Contains(McpConstants.ArgDatabase, tool.InputSchema.Required);
        Assert.Contains(McpConstants.ArgQuery, tool.InputSchema.Required);
        Assert.Contains(McpConstants.ArgOutputPath, tool.InputSchema.Required);
        Assert.Equal("string", tool.InputSchema.Properties[McpConstants.ArgDatabase].Type);
        Assert.Equal("string", tool.InputSchema.Properties[McpConstants.ArgQuery].Type);
        Assert.Equal("string", tool.InputSchema.Properties[McpConstants.ArgOutputPath].Type);
        Assert.Equal("string", tool.InputSchema.Properties[McpConstants.ArgFormat].Type);
        Assert.Equal("boolean", tool.InputSchema.Properties[McpConstants.ArgOverwrite].Type);
        Assert.Equal("integer", tool.InputSchema.Properties[McpConstants.ArgRequestedRowLimit].Type);
        Assert.Equal("object", tool.InputSchema.Properties[McpConstants.ArgParameters].Type);
    }

    [Fact]
    public void ExecutionTools_ShouldDescribeValidationAndAtomicityLimitsInLiveMetadata()
    {
        var tools = SqlMcpToolRegistrations.BuildToolCollection(ToolDispatcherTestHelper.BuildDispatcher());
        var validation = tools.Single(t => t.ProtocolTool.Name == McpConstants.ToolValidateQuery).ProtocolTool;
        var script = tools.Single(t => t.ProtocolTool.Name == McpConstants.ToolExecuteFile).ProtocolTool;

        Assert.Contains("NOEXEC compilation", validation.Description, StringComparison.Ordinal);
        Assert.Contains("Deferred name resolution", validation.Description, StringComparison.Ordinal);
        Assert.Contains("atomicity is not guaranteed", script.Description, StringComparison.Ordinal);
        Assert.Contains("dynamic SQL and triggers", script.Description, StringComparison.Ordinal);
        Assert.Equal(_registry.GetAll().Single(t => t.Name == validation.Name).Description, validation.Description);
        Assert.Equal(_registry.GetAll().Single(t => t.Name == script.Name).Description, script.Description);
        string? transactionDescription = script.InputSchema.GetProperty("properties")
            .GetProperty(McpConstants.ArgUseTransaction).GetProperty("description").GetString();
        Assert.Contains("preserve that transaction", transactionDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void AllToolNames_ShouldBeUnique()
    {
        var names = _registry.GetAll().Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
