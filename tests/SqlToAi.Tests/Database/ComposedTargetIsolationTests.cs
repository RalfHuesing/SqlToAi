using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Metadata;
using SqlToAi.Security;
using SqlToAi.Tests.Mcp;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Milestone 4.2 composed acceptance tests:
/// 1. Alternating and parallel calls across multiple targets with the same raw database name
///    never mix endpoints, access rights, rules, cache, or token scopes.
/// 2. The same physical endpoint configured under distinct aliases retains independent owners.
/// 3. Syntactically unusual raw database names (dots, spaces, dashes, brackets, unicode)
///    flow uncorrupted through resolution, access checks, exclusions, and connections.
/// 4. Server discovery (sql_list_servers) performs zero database or SQL probes.
/// 5. Actual MCP SDK registration and argument binding covers all 19 tools and enforces
///    qualified database targets.
/// </summary>
public sealed class ComposedTargetIsolationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class ConfigurableRuleProvider : IAnonymizationRuleProvider
    {
        private readonly HashSet<string> _exemptions = new(StringComparer.OrdinalIgnoreCase);

        public void AddExemption(string serverAlias, string databaseName, string columnName)
        {
            _exemptions.Add($"{serverAlias}/{databaseName}:{columnName}");
        }

        public Task<bool> IsExcludedAsync(DatabaseTargetContext target, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
        {
            bool isExempt = _exemptions.Contains($"{target.ServerAlias}/{target.DatabaseName}:{columnName}");
            return Task.FromResult(isExempt);
        }
    }

    // =========================================================================
    // 1. Alternating and parallel calls across multiple targets
    // =========================================================================

    [Fact]
    public async Task MultiTargetParallelAndAlternating_NeverMixesEndpointsRightsRulesCacheOrTokens()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Tokenization.Enabled = true;
        options.Servers["alpha"] = new NamedServerOptions
        {
            Server = "srv-alpha.corp.local",
            TrustServerCertificate = true,
            AnonymizationRules = { Enabled = true }
        };
        options.Servers["beta"] = new NamedServerOptions
        {
            Server = "srv-beta.corp.local",
            TrustServerCertificate = true,
            AnonymizationRules = { Enabled = false }
        };

        // Same database name "InventoryDb" under both servers, but different access levels
        options.Databases.ReadOnlyAnonymized.Add("alpha/InventoryDb");
        options.Databases.ReadOnly.Add("beta/InventoryDb");

        var rowsByTarget = new Dictionary<string, TargetRowSet>(StringComparer.Ordinal)
        {
            ["alpha/InventoryDb"] = new TargetRowSet(["ExemptValue", "Secret"], [["SKU-ALPHA", "alpha-secret"]]),
            ["beta/InventoryDb"] = new TargetRowSet(["ExemptValue", "Secret"], [["SKU-BETA", "beta-secret"]])
        };

        var factory = new TargetRowsConnectionFactory(rowsByTarget);
        var barrier = new OwnerOverlapBarrier("alpha/InventoryDb", "beta/InventoryDb");
        factory.OpenBarrier = barrier;

        var opts = Options.Create(options);
        var security = new SecurityGuard(opts);
        var access = new GrantsAccessLevelProvider(options);
        var vault = new TokenVault();
        var tokenResolver = new QueryTokenResolver(vault, opts);
        var validator = new QuerySafetyValidator(security, access, new ReadOnlyGuard());

        var ruleProvider = new ConfigurableRuleProvider();
        ruleProvider.AddExemption("alpha", "InventoryDb", "ExemptValue");

        var anonymizer = new Anonymizer(opts, vault);
        var execution = new QueryExecutionService(
            factory,
            validator,
            new AnonymizationDependencies(anonymizer, ruleProvider, tokenResolver),
            opts,
            NullLogger<QueryExecutionService>.Instance);

        // Launch 24 concurrent requests alternating between alpha and beta
        var tasks = Enumerable.Range(0, 24).Select(i => Task.Run(async () =>
        {
            bool isAlpha = i % 2 == 0;
            string target = isAlpha ? "alpha/InventoryDb" : "beta/InventoryDb";
            var result = await execution.ExecuteQueryAsync(target, "SELECT ExemptValue, Secret FROM T", null, Token);
            return (isAlpha, result);
        })).ToList();

        var results = await Task.WhenAll(tasks);
        Assert.True(barrier.CrossOwnerOverlapReached, "Cross-owner overlap barrier was not triggered");

        foreach (var (isAlpha, result) in results)
        {
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

            if (isAlpha)
            {
                // Alpha: ReadOnlyAnonymized. ExemptValue is exempt (raw), Secret is tokenized
                Assert.True(result.Value.WasAnonymized);
                Assert.Contains("SKU-ALPHA", result.Value.Data, StringComparison.Ordinal);
                Assert.DoesNotContain("alpha-secret", result.Value.Data, StringComparison.Ordinal);
                string secret = ExtractColumnValue(result.Value.Data, "Secret");
                Assert.StartsWith(options.Anonymizer.Tokenization.Prefix, secret, StringComparison.Ordinal);
            }
            else
            {
                // Beta: ReadOnly. Both columns remain raw
                Assert.False(result.Value.WasAnonymized);
                Assert.Contains("SKU-BETA", result.Value.Data, StringComparison.Ordinal);
                Assert.Contains("beta-secret", result.Value.Data, StringComparison.Ordinal);
                string secret = ExtractColumnValue(result.Value.Data, "Secret");
                Assert.False(secret.StartsWith(options.Anonymizer.Tokenization.Prefix, StringComparison.Ordinal));
            }
        }

        // Verify tokens are strictly owner-scoped
        var alphaEgress = await execution.ExecuteQueryAsync("alpha/InventoryDb", "SELECT ExemptValue, Secret FROM T", null, Token);
        string token = ExtractColumnValue(alphaEgress.Value.Data, "Secret");

        // Token substitutes in Alpha
        await execution.ExecuteQueryAsync("alpha/InventoryDb", $"SELECT * FROM T WHERE Secret = '{token}'", null, Token);
        Assert.Contains("alpha-secret", factory.LastConnection?.LastCommand?.CommandText, StringComparison.Ordinal);

        // Same token in Beta is foreign and stays literal SQL text
        await execution.ExecuteQueryAsync("beta/InventoryDb", $"SELECT * FROM T WHERE Secret = '{token}'", null, Token);
        Assert.Contains(token, factory.LastConnection?.LastCommand?.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha-secret", factory.LastConnection?.LastCommand?.CommandText, StringComparison.Ordinal);
    }

    // =========================================================================
    // 2. Same physical endpoint under two aliases has independent owners
    // =========================================================================

    [Fact]
    public async Task SamePhysicalEndpointUnderTwoAliases_RetainsIndependentOwners()
    {
        var options = new SqlToAiOptions();
        options.Servers["alias-one"] = new NamedServerOptions
        {
            Server = "shared-sql.corp.local",
            ConnectTimeoutSeconds = 10,
            ExcludedDatabases = []
        };
        options.Servers["alias-two"] = new NamedServerOptions
        {
            Server = "shared-sql.corp.local",
            ConnectTimeoutSeconds = 10,
            ExcludedDatabases = ["FinanceDb"] // excluded specifically on alias-two
        };

        // Same raw database name granted to alias-one, but excluded on alias-two
        options.Databases.ReadOnly.Add("alias-one/FinanceDb");
        options.Databases.ReadOnly.Add("alias-two/FinanceDb");

        var rowsByTarget = new Dictionary<string, TargetRowSet>(StringComparer.Ordinal)
        {
            ["alias-one/FinanceDb"] = new TargetRowSet(["Amount"], [[1000]]),
            ["alias-two/FinanceDb"] = new TargetRowSet(["Amount"], [[2000]])
        };

        var factory = new TargetRowsConnectionFactory(rowsByTarget);
        var opts = Options.Create(options);
        var security = new SecurityGuard(opts);
        var access = new GrantsAccessLevelProvider(options);
        var validator = new QuerySafetyValidator(security, access, new ReadOnlyGuard());
        var vault = new TokenVault();
        var execution = new QueryExecutionService(
            factory,
            validator,
            new AnonymizationDependencies(new Anonymizer(opts, vault), new TargetRuleProvider(), new QueryTokenResolver(vault, opts)),
            opts,
            NullLogger<QueryExecutionService>.Instance);

        // alias-one/FinanceDb is permitted and succeeds
        var successResult = await execution.ExecuteQueryAsync("alias-one/FinanceDb", "SELECT Amount FROM T", null, Token);
        Assert.True(successResult.IsSuccess);
        Assert.Contains("1000", successResult.Value.Data, StringComparison.Ordinal);

        // alias-two/FinanceDb is denied by alias-two's ExcludedDatabases before any connection
        var deniedResult = await execution.ExecuteQueryAsync("alias-two/FinanceDb", "SELECT Amount FROM T", null, Token);
        Assert.True(deniedResult.IsFailure);
        Assert.Equal("SQL-AI-0104", deniedResult.Error.Code);

        // Created targets in factory should only contain alias-one
        Assert.All(factory.CreatedTargets, target => Assert.Equal("alias-one", target.ServerAlias));
    }

    // =========================================================================
    // 3. Syntactically unusual raw database names flow uncorrupted
    // =========================================================================

    [Theory]
    [InlineData("Accounting.2026.Q1")]
    [InlineData("Human Resources")]
    [InlineData("app-stage-01")]
    [InlineData("[Special.Db]")]
    [InlineData("München_Finanz")]
    [InlineData("Sub/Division/Dept")]
    public async Task SyntacticallyUnusualRawDatabaseNames_FlowUncorruptedThroughServices(string rawDbName)
    {
        var options = new SqlToAiOptions();
        options.Servers["prod"] = new NamedServerOptions
        {
            Server = "prod-sql.corp.local",
            ConnectTimeoutSeconds = 15
        };

        string qualifiedTarget = $"prod/{rawDbName}";
        options.Databases.ReadOnly.Add(qualifiedTarget);

        var rowsByTarget = new Dictionary<string, TargetRowSet>(StringComparer.Ordinal)
        {
            [qualifiedTarget] = new TargetRowSet(["Value"], [["UnusualDbData"]])
        };

        var factory = new TargetRowsConnectionFactory(rowsByTarget);
        var opts = Options.Create(options);
        var security = new SecurityGuard(opts);
        var access = new GrantsAccessLevelProvider(options);
        var validator = new QuerySafetyValidator(security, access, new ReadOnlyGuard());
        var vault = new TokenVault();
        var execution = new QueryExecutionService(
            factory,
            validator,
            new AnonymizationDependencies(new Anonymizer(opts, vault), new TargetRuleProvider(), new QueryTokenResolver(vault, opts)),
            opts,
            NullLogger<QueryExecutionService>.Instance);

        // Resolver check
        var resolver = new DatabaseTargetResolver(options);
        var targetResult = resolver.Resolve(qualifiedTarget);
        Assert.True(targetResult.IsSuccess);
        Assert.Equal("prod", targetResult.Value.ServerAlias);
        Assert.Equal(rawDbName, targetResult.Value.DatabaseName);
        Assert.Equal(qualifiedTarget, targetResult.Value.QualifiedName);

        // Access level check
        var accessLevel = await access.GetTargetAccessLevelAsync(targetResult.Value, Token);
        Assert.Equal(AccessLevel.ReadOnly, accessLevel);

        // Query execution check
        var queryResult = await execution.ExecuteQueryAsync(qualifiedTarget, "SELECT Value FROM T", null, Token);
        Assert.True(queryResult.IsSuccess, queryResult.IsFailure ? queryResult.Error.Message : string.Empty);
        Assert.Contains("UnusualDbData", queryResult.Value.Data, StringComparison.Ordinal);

        // Verify the factory received the exact raw database name
        var lastTarget = factory.CreatedTargets.Last();
        Assert.Equal(rawDbName, lastTarget.DatabaseName);
    }

    // =========================================================================
    // 4. Server discovery performs zero SQL probes
    // =========================================================================

    [Fact]
    public void ServerDiscovery_PerformsZeroDatabaseCalls()
    {
        var options = new SqlToAiOptions();
        options.Servers["srv-1"] = new NamedServerOptions { Server = "host1" };
        options.Servers["srv-2"] = new NamedServerOptions { Server = "host2" };
        options.Servers["srv-3"] = new NamedServerOptions { Server = "host3" };

        var factory = new TargetRowsConnectionFactory([]);
        var opts = Options.Create(options);
        var security = new SecurityGuard(opts);
        var access = new GrantsAccessLevelProvider(options);
        var metadata = new MetadataProvider(factory, opts, NullLogger<MetadataProvider>.Instance);
        var policy = new AlwaysAllowPolicyResolver();
        var schema = new SchemaService(factory, security, access, metadata, policy, opts, NullLogger<SchemaService>.Instance);

        var result = schema.ListServers();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        Assert.Contains("srv-1", result.Value);
        Assert.Contains("srv-2", result.Value);
        Assert.Contains("srv-3", result.Value);
        Assert.Empty(factory.CreatedTargets);
    }

    // =========================================================================
    // 5. Actual MCP SDK registration and argument binding for all 19 tools
    // =========================================================================

    [Fact]
    public void SdkToolRegistrations_ExposesExactNineteenTools()
    {
        var dispatcher = ToolDispatcherTestHelper.BuildDispatcher();
        var toolCollection = SqlMcpToolRegistrations.BuildToolCollection(dispatcher);

        Assert.Equal(19, toolCollection.Count);

        var toolNames = toolCollection.Select(t => t.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(McpConstants.ToolListServers, toolNames);
        Assert.Contains(McpConstants.ToolListDatabases, toolNames);
        Assert.Contains(McpConstants.ToolSearchDatabases, toolNames);
        Assert.Contains(McpConstants.ToolValidateQuery, toolNames);
        Assert.Contains(McpConstants.ToolSearchObjects, toolNames);
        Assert.Contains(McpConstants.ToolGetSchema, toolNames);
        Assert.Contains(McpConstants.ToolExportSchema, toolNames);
        Assert.Contains(McpConstants.ToolGetSchemaForeignKeys, toolNames);
        Assert.Contains(McpConstants.ToolGetSchemaIndexes, toolNames);
        Assert.Contains(McpConstants.ToolGetSchemaConstraints, toolNames);
        Assert.Contains(McpConstants.ToolGetTriggerDefinition, toolNames);
        Assert.Contains(McpConstants.ToolGetObjectReferences, toolNames);
        Assert.Contains(McpConstants.ToolGetRoutineParameters, toolNames);
        Assert.Contains(McpConstants.ToolExecuteQuery, toolNames);
        Assert.Contains(McpConstants.ToolExecuteFile, toolNames);
        Assert.Contains(McpConstants.ToolCompareQueries, toolNames);
        Assert.Contains(McpConstants.ToolMeasurePerformance, toolNames);
        Assert.Contains(McpConstants.ToolBenchmarkOptimization, toolNames);
        Assert.Contains(McpConstants.ToolSuggestIndexes, toolNames);
    }

    [Fact]
    public async Task SdkToolBinding_ListServers_RequiresNoArgsAndSucceeds()
    {
        var schema = new FakeSchemaService();
        var dispatcher = ToolDispatcherTestHelper.BuildDispatcher(schema);
        var toolCollection = SqlMcpToolRegistrations.BuildToolCollection(dispatcher);
        var tool = toolCollection.Single(t => t.ProtocolTool.Name == McpConstants.ToolListServers);

        await using var transport = new StreamServerTransport(new MemoryStream(), new MemoryStream());
        await using var server = McpServer.Create(transport, new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "test-server", Version = "1.0" }
        });

        var context = new RequestContext<CallToolRequestParams>(
            server,
            new JsonRpcRequest { Id = new RequestId(1), Method = "tools/call" },
            new CallToolRequestParams { Name = McpConstants.ToolListServers, Arguments = new Dictionary<string, JsonElement>() });

        var result = await tool.InvokeAsync(context, Token);

        Assert.False(result.IsError);
        Assert.True(schema.ListServersCalled);
    }

    [Fact]
    public async Task SdkToolBinding_ExecuteQuery_RejectsUnqualifiedDatabaseTarget()
    {
        var dispatcher = ToolDispatcherTestHelper.BuildDispatcher(exec: new FakeQueryExecutionService(validateTarget: true));
        var toolCollection = SqlMcpToolRegistrations.BuildToolCollection(dispatcher);
        var tool = toolCollection.Single(t => t.ProtocolTool.Name == McpConstants.ToolExecuteQuery);

        await using var transport = new StreamServerTransport(new MemoryStream(), new MemoryStream());
        await using var server = McpServer.Create(transport, new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "test-server", Version = "1.0" }
        });

        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpConstants.ArgDatabase] = JsonSerializer.SerializeToElement("UnqualifiedDb"),
            [McpConstants.ArgQuery] = JsonSerializer.SerializeToElement("SELECT 1")
        };

        var context = new RequestContext<CallToolRequestParams>(
            server,
            new JsonRpcRequest { Id = new RequestId(1), Method = "tools/call" },
            new CallToolRequestParams { Name = McpConstants.ToolExecuteQuery, Arguments = arguments });

        var result = await tool.InvokeAsync(context, Token);

        Assert.True(result.IsError);
        Assert.Contains("SQL-AI-0001", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    private static string ExtractColumnValue(string jsonLinesData, string columnName)
    {
        string firstLine = jsonLinesData.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        using var doc = JsonDocument.Parse(firstLine);
        return doc.RootElement.GetProperty(columnName).GetString()!;
    }
}
