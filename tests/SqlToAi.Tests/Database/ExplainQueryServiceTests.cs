using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class ExplainQueryServiceTests
{
    private const string SampleShowplanXml = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.5" Build="16.0.4180.1">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple StatementText="SELECT 1" StatementId="1" StatementCompId="1" StatementType="SELECT">
                  <QueryPlan DegreeOfParallelism="1">
                    <RelOp NodeId="0" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateIO="0" EstimateCPU="1.157e-06" AvgRowSize="9" EstimatedTotalSubtreeCost="1.157e-06" Parallel="0" EstimateRebinds="0" EstimateRewinds="0">
                      <OutputList />
                      <ConstantScan />
                    </RelOp>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

    private sealed class TrackingShowplanExecutionService : IShowplanExecutionService
    {
        public int CallCount { get; private set; }
        public DatabaseTargetContext? LastTarget { get; private set; }
        public string? LastQuery { get; private set; }
        public object? LastParameters { get; private set; }
        public Result<string>? NextResult { get; set; }

        public Task<Result<string>> ExecuteShowplanAsync(
            string databaseName, string query, object? parameters = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(NextResult ?? Result<string>.Success(SampleShowplanXml));
        }

        public Task<Result<string>> ExecuteShowplanAsync(
            DatabaseTargetContext target, string query, object? parameters = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastTarget = target;
            LastQuery = query;
            LastParameters = parameters;
            return Task.FromResult(NextResult ?? Result<string>.Success(SampleShowplanXml));
        }
    }

    private sealed class TrackingAccessLevelProvider : IAccessLevelProvider
    {
        public int CallCount { get; private set; }
        public AccessLevel LevelToReturn { get; set; } = AccessLevel.ReadOnly;

        public Task<AccessLevel> GetTargetAccessLevelAsync(DatabaseTargetContext target, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(LevelToReturn);
        }

        public Task<AccessLevel> GetAccessLevelAsync(string databaseName, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(LevelToReturn);
        }
    }

    private static (ExplainQueryService Service, TrackingShowplanExecutionService ExecService, TrackingAccessLevelProvider AccessProvider)
        CreateService(Action<SqlToAiOptions>? configure = null, AccessLevel defaultAccess = AccessLevel.ReadOnly)
    {
        var options = new SqlToAiOptions()
            .WithNamedServers("srv1", "srv2");
        options.Databases.ReadOnly = ["srv1/SharedDb", "srv2/SharedDb", "srv1/WhitelistedDb"];
        options.Servers["srv1"].ExcludedDatabases = ["ExcludedDb"];
        configure?.Invoke(options);

        var bound = Options.Create(options);
        var guard = new SecurityGuard(bound);
        var readOnlyGuard = new ReadOnlyGuard();
        var statementGuard = new ExplainQueryStatementGuard(guard, readOnlyGuard);
        var execService = new TrackingShowplanExecutionService();
        var parser = new ShowplanXmlParser(bound);
        var accessProvider = new TrackingAccessLevelProvider { LevelToReturn = defaultAccess };

        var service = new ExplainQueryService(
            execService,
            parser,
            statementGuard,
            guard,
            accessProvider,
            bound,
            NullLogger<ExplainQueryService>.Instance);

        return (service, execService, accessProvider);
    }

    // =========================================================================
    // 1. Target resolution before any connection
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExplainQuery_EmptyDatabaseName_ReturnsInvalidParameters_WithoutConnecting(string db)
    {
        var (service, exec, access) = CreateService();
        var result = await service.ExplainQueryAsync(db, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Equal(0, exec.CallCount);
        Assert.Equal(0, access.CallCount);
    }

    [Theory]
    [InlineData("SharedDb")]
    [InlineData("dbo.Customers")]
    [InlineData("unknown_alias/SharedDb")]
    public async Task ExplainQuery_MalformedOrUnknownTarget_ReturnsCatalogError_WithoutConnecting(string target)
    {
        var (service, exec, access) = CreateService();
        var result = await service.ExplainQueryAsync(target, "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Equal(0, exec.CallCount);
        Assert.Equal(0, access.CallCount);
    }

    [Fact]
    public async Task ExplainQuery_ExcludedDatabase_ReturnsSafetyCheckFailed_WithoutConnecting()
    {
        var (service, exec, access) = CreateService();
        var result = await service.ExplainQueryAsync("srv1/ExcludedDb", "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Equal(0, exec.CallCount);
        Assert.Equal(0, access.CallCount);
    }

    // =========================================================================
    // 2. Statement allowlist before any connection
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExplainQuery_EmptyQuery_ReturnsInvalidParameters_WithoutConnecting(string query)
    {
        var (service, exec, access) = CreateService();
        var result = await service.ExplainQueryAsync("srv1/SharedDb", query, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Equal(0, exec.CallCount);
        Assert.Equal(0, access.CallCount);
    }

    [Theory]
    [InlineData("INSERT INTO Customers (Name) VALUES ('Test')")]
    [InlineData("UPDATE Customers SET Name = 'Test' WHERE Id = 1")]
    [InlineData("DELETE FROM Customers WHERE Id = 1")]
    [InlineData("MERGE Customers AS target USING Source AS src ON target.Id = src.Id WHEN MATCHED THEN UPDATE SET Name = src.Name;")]
    [InlineData("SELECT * INTO NewTable FROM Customers")]
    [InlineData("DROP TABLE Customers")]
    [InlineData("EXEC sp_who2")]
    public async Task ExplainQuery_Mutations_RejectedAcrossAllAccessLevels_WithoutConnecting(string mutatingQuery)
    {
        // Even when access level would permit writes (ReadWrite or ReadWriteAnonymized),
        // mutating queries must fail with SQL-AI-0107 before any connection or access level check.
        foreach (var level in new[] { AccessLevel.ReadOnly, AccessLevel.SchemaOnly, AccessLevel.ReadWrite, AccessLevel.ReadWriteAnonymized })
        {
            var (service, exec, access) = CreateService(defaultAccess: level);
            var result = await service.ExplainQueryAsync("srv1/SharedDb", mutatingQuery, null, TestContext.Current.CancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
            Assert.Equal(0, exec.CallCount);
            Assert.Equal(0, access.CallCount);
        }
    }

    [Theory]
    [InlineData("SET SHOWPLAN_XML ON; SELECT 1")]
    [InlineData("SET NOEXEC ON; SELECT 1")]
    [InlineData("SET STATISTICS IO ON; SELECT 1")]
    [InlineData("SELECT 1; SET SHOWPLAN_XML OFF")]
    [InlineData("BEGIN TRANSACTION; SELECT 1")]
    [InlineData("USE OtherDb; SELECT 1")]
    public async Task ExplainQuery_Controls_RejectedWithSafetyCheckFailed_WithoutConnecting(string controlQuery)
    {
        var (service, exec, access) = CreateService();
        var result = await service.ExplainQueryAsync("srv1/SharedDb", controlQuery, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Equal(0, exec.CallCount);
        Assert.Equal(0, access.CallCount);
    }

    [Fact]
    public async Task ExplainQuery_MultipleStatements_RejectedWithMultipleStatementsForbidden_WithoutConnecting()
    {
        var (service, exec, access) = CreateService();
        var result = await service.ExplainQueryAsync("srv1/SharedDb", "SELECT 1; SELECT 2", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, result.Error.Code);
        Assert.Equal(0, exec.CallCount);
        Assert.Equal(0, access.CallCount);
    }

    [Fact]
    public async Task ExplainQuery_UnparseableSql_RejectedWithQueryError_WithoutConnecting()
    {
        var (service, exec, access) = CreateService();
        var result = await service.ExplainQueryAsync("srv1/SharedDb", "SELECT FROM WHERE !!!", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Equal(0, exec.CallCount);
        Assert.Equal(0, access.CallCount);
    }

    // =========================================================================
    // 3. Access levels (None rejected before execution; SchemaOnly, Anonymized, ReadOnly, ReadWrite allowed)
    // =========================================================================

    [Fact]
    public async Task ExplainQuery_AccessLevelNone_RejectedWithSafetyCheckFailed_WithoutShowplanConnection()
    {
        var (service, exec, access) = CreateService(defaultAccess: AccessLevel.None);
        var result = await service.ExplainQueryAsync("srv1/SharedDb", "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Equal(0, exec.CallCount);
        Assert.Equal(1, access.CallCount);
    }

    [Theory]
    [InlineData(AccessLevel.SchemaOnly)]
    [InlineData(AccessLevel.ReadOnlyAnonymized)]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWriteAnonymized)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ExplainQuery_PermitsAllNonNoneAccessLevels(AccessLevel level)
    {
        var (service, exec, access) = CreateService(defaultAccess: level);
        var result = await service.ExplainQueryAsync("srv1/SharedDb", "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, exec.CallCount);
        Assert.Equal(1, access.CallCount);
        Assert.Equal("srv1/SharedDb", result.Value.Target);
        Assert.Equal("estimated", result.Value.PlanKind);
    }

    // =========================================================================
    // 4. Execution and Parsing orchestration
    // =========================================================================

    [Fact]
    public async Task ExplainQuery_ValidQueryAndParameters_PassedToShowplanExecutionAndParsed()
    {
        var (service, exec, _) = CreateService();
        var parameters = new Dictionary<string, object?> { ["id"] = 123 };
        const string query = "DECLARE @p int = 123; SELECT * FROM Customers WHERE Id = @p";

        var result = await service.ExplainQueryAsync("srv1/SharedDb", query, parameters, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, exec.CallCount);
        Assert.Equal("srv1/SharedDb", exec.LastTarget?.QualifiedName);
        Assert.Equal(query, exec.LastQuery);
        Assert.Equal(parameters, exec.LastParameters);

        var plan = result.Value;
        Assert.Equal("srv1/SharedDb", plan.Target);
        Assert.Equal("estimated", plan.PlanKind);
        Assert.NotEmpty(plan.Nodes);
        Assert.Null(plan.Nodes[0].ParentId);
    }

    [Fact]
    public async Task ExplainQuery_ShowplanExecutionFailure_PropagatesError()
    {
        var (service, exec, _) = CreateService();
        exec.NextResult = SqlToAiError.SafetyCheckFailed("SHOWPLAN permission is required to view execution plans.");

        var result = await service.ExplainQueryAsync("srv1/SharedDb", "SELECT 1", null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Contains("SHOWPLAN permission", result.Error.Message);
    }
}
