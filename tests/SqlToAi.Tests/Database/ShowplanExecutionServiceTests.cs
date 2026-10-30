using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.TestSupport;
using Xunit;

namespace SqlToAi.Tests.Database;

public sealed class ShowplanExecutionServiceTests
{
    private const string SampleShowplanXml = """
        <?xml version="1.0" encoding="utf-16"?>
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.564" Build="16.0.4180.1">
          <BatchSequence>
            <Batch>
              <Statements>
                <StmtSimple StatementText="SELECT CustomerID FROM dbo.Customers WHERE CustomerID = @id" StatementType="SELECT">
                  <QueryPlan>
                    <RelOp NodeId="0" PhysicalOp="Clustered Index Seek" LogicalOp="Clustered Index Seek" EstimateRows="1" EstimatedTotalSubtreeCost="0.0032831">
                    </RelOp>
                  </QueryPlan>
                </StmtSimple>
              </Statements>
            </Batch>
          </BatchSequence>
        </ShowPlanXML>
        """;

    private sealed class TrackingShowplanConnection : DbConnection
    {
        private readonly Func<TrackingShowplanConnection, DbCommand> _commandFactory;
        private ConnectionState _state = ConnectionState.Closed;

        public TrackingShowplanConnection(Func<TrackingShowplanConnection, DbCommand> commandFactory)
        {
            _commandFactory = commandFactory;
        }

        public bool IsDisposed { get; private set; }
        public int DisposeCount { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "TestDb";
        public override string DataSource => "TestServer";
        public override string ServerVersion => "16.0";
        public override ConnectionState State => _state;

        public override void Open() => _state = ConnectionState.Open;

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            _state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        public override void Close() => _state = ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName) { }

        protected override DbCommand CreateDbCommand() => _commandFactory(this);

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException("No transaction should be opened during showplan execution.");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
                DisposeCount++;
                _state = ConnectionState.Closed;
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            IsDisposed = true;
            DisposeCount++;
            _state = ConnectionState.Closed;
            await base.DisposeAsync();
        }
    }

    private sealed class ExecutedCommandRecord(string commandText, CommandType commandType, int commandTimeout, Dictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        public string CommandText { get; } = commandText;
        public CommandType CommandType { get; } = commandType;
        public int CommandTimeout { get; } = commandTimeout;
        public Dictionary<string, object?> Parameters { get; } = parameters;
        public CancellationToken CancellationToken { get; } = cancellationToken;
    }

    private sealed class ShowplanTestHarness
    {
        public TrackingShowplanConnection? LastConnection { get; private set; }
        public List<ExecutedCommandRecord> ExecutedCommands { get; } = [];
        public List<bool> RequestedPoolings { get; } = [];
        public Func<DbCommand, CancellationToken, Task<DbDataReader>>? CustomReaderHandler { get; set; }
        public Func<DbCommand, CancellationToken, Task<int>>? CustomNonQueryHandler { get; set; }
        public Exception? ConnectionOpenFailure { get; set; }

        public IDatabaseConnectionFactory CreateFactory() => new TestFactory(this);

        private sealed class TestFactory(ShowplanTestHarness harness) : IDatabaseConnectionFactory
        {
            public DbConnection CreateTargetConnection(DatabaseTargetContext target) =>
                CreateTargetConnection(target, pooling: true);

            public DbConnection CreateTargetConnection(DatabaseTargetContext target, bool pooling)
            {
                harness.RequestedPoolings.Add(pooling);
                if (harness.ConnectionOpenFailure is not null)
                {
                    throw harness.ConnectionOpenFailure;
                }

                harness.LastConnection = new TrackingShowplanConnection(conn =>
                {
                    return new FakeDbCommand(conn, new FakeDbCommandHandlers(
                        ExecuteNonQueryAsync: (cmd, ct) =>
                        {
                            var dict = new Dictionary<string, object?>();
                            foreach (DbParameter p in cmd.Parameters)
                            {
                                dict[p.ParameterName] = p.Value;
                            }
                            harness.ExecutedCommands.Add(new ExecutedCommandRecord(cmd.CommandText, cmd.CommandType, cmd.CommandTimeout, dict, ct));
                            return harness.CustomNonQueryHandler is not null
                                ? harness.CustomNonQueryHandler(cmd, ct)
                                : Task.FromResult(0);
                        },
                        ExecuteReaderAsync: (cmd, ct) =>
                        {
                            var dict = new Dictionary<string, object?>();
                            foreach (DbParameter p in cmd.Parameters)
                            {
                                dict[p.ParameterName] = p.Value;
                            }
                            harness.ExecutedCommands.Add(new ExecutedCommandRecord(cmd.CommandText, cmd.CommandType, cmd.CommandTimeout, dict, ct));
                            if (harness.CustomReaderHandler is not null)
                            {
                                return harness.CustomReaderHandler(cmd, ct);
                            }
                            var reader = new FakeDbDataReader([new FakeDbResultSet(["Microsoft SQL Server 2005 XML Showplan"], [ [SampleShowplanXml] ])]);
                            return Task.FromResult<DbDataReader>(reader);
                        }));
                });

                return harness.LastConnection;
            }

            public Result<DbConnection> CreateCatalogConnection(string? serverAlias) =>
                Result<DbConnection>.Failure(SqlToAiError.InvalidParameters("Not supported"));
        }
    }

    private static (ShowplanExecutionService Service, ShowplanTestHarness Harness) CreateService(Action<SqlToAiOptions>? configure = null)
    {
        var options = new SqlToAiOptions().WithNamedServers("srv1");
        options.Databases.ReadOnly = ["srv1/TestDb"];
        options.QueryExecution.CommandTimeoutSeconds = 45;
        options.ExplainQuery.MaxShowplanXmlBytes = 2 * 1024 * 1024;
        configure?.Invoke(options);

        var bound = Options.Create(options);
        var harness = new ShowplanTestHarness();
        var service = new ShowplanExecutionService(harness.CreateFactory(), bound, NullLogger<ShowplanExecutionService>.Instance);
        return (service, harness);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_ValidQuery_ExecutesBatchesInOrderAndDisposesConnection()
    {
        var (service, harness) = CreateService();
        var target = new DatabaseTargetContext("srv1", "TestDb");
        var parameters = new Dictionary<string, object?> { ["id"] = "ALFKI" };

        var result = await service.ExecuteShowplanAsync(
            target,
            "SELECT CustomerID FROM dbo.Customers WHERE CustomerID = @id",
            parameters,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains("<ShowPlanXML", result.Value);

        // Batch order verification:
        // 1. SET SHOWPLAN_XML ON
        // 2. Query with bound parameter
        // 3. SET SHOWPLAN_XML OFF
        Assert.Equal(3, harness.ExecutedCommands.Count);
        Assert.Equal("SET SHOWPLAN_XML ON", harness.ExecutedCommands[0].CommandText);
        Assert.Equal(45, harness.ExecutedCommands[0].CommandTimeout);

        Assert.Equal("SELECT CustomerID FROM dbo.Customers WHERE CustomerID = @id", harness.ExecutedCommands[1].CommandText);
        Assert.Equal("ALFKI", harness.ExecutedCommands[1].Parameters["@id"]);
        Assert.Equal(45, harness.ExecutedCommands[1].CommandTimeout);

        Assert.Equal("SET SHOWPLAN_XML OFF", harness.ExecutedCommands[2].CommandText);

        // Connection disposal verification:
        Assert.NotNull(harness.LastConnection);
        Assert.True(harness.LastConnection.IsDisposed);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_RequestsNonPooledConnection()
    {
        var (service, harness) = CreateService();
        var target = new DatabaseTargetContext("srv1", "TestDb");

        var result = await service.ExecuteShowplanAsync(target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(harness.RequestedPoolings);
        Assert.False(harness.RequestedPoolings[0]);
    }

    [Fact]
    public void SqlServerConnectionFactory_EnforcesPoolingFalse_WhenRequested()
    {
        var options = new SqlToAiOptions().WithNamedServers("srv1");
        var bound = Options.Create(options);
        var factory = new SqlConnectionFactory(bound);
        var target = new DatabaseTargetContext("srv1", "TestDb");

        var nonPooledConn = factory.CreateTargetConnection(target, pooling: false);
        Assert.Contains("Pooling=False", nonPooledConn.ConnectionString, StringComparison.OrdinalIgnoreCase);

        var pooledConn = factory.CreateTargetConnection(target, pooling: true);
        Assert.DoesNotContain("Pooling=False", pooledConn.ConnectionString, StringComparison.OrdinalIgnoreCase);

        var defaultConn = factory.CreateTargetConnection(target);
        Assert.DoesNotContain("Pooling=False", defaultConn.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_OnQueryError_AttemptsResetAndDisposesConnection()
    {
        var (service, harness) = CreateService();
        var target = new DatabaseTargetContext("srv1", "TestDb");
        harness.CustomReaderHandler = (_, _) => throw SqlExceptionFactory.Create(102, "Incorrect syntax near 'BROKEN'.");

        var result = await service.ExecuteShowplanAsync(target, "SELECT BROKEN", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);

        // Reset must be attempted in finally
        Assert.Equal(3, harness.ExecutedCommands.Count);
        Assert.Equal("SET SHOWPLAN_XML ON", harness.ExecutedCommands[0].CommandText);
        Assert.Equal("SELECT BROKEN", harness.ExecutedCommands[1].CommandText);
        Assert.Equal("SET SHOWPLAN_XML OFF", harness.ExecutedCommands[2].CommandText);

        Assert.NotNull(harness.LastConnection);
        Assert.True(harness.LastConnection.IsDisposed);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_OnCancellation_UsesIndependentCleanupTokenAndDisposesConnection()
    {
        var (service, harness) = CreateService();
        var target = new DatabaseTargetContext("srv1", "TestDb");
        using var cts = new CancellationTokenSource();

        harness.CustomReaderHandler = async (_, ct) =>
        {
            await cts.CancelAsync();
            throw new OperationCanceledException(ct);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.ExecuteShowplanAsync(target, "SELECT 1", cancellationToken: cts.Token);
        });

        // Verification of independent cleanup token:
        // Even though caller token was cancelled, SET SHOWPLAN_XML OFF got an uncancelled token!
        Assert.Equal(3, harness.ExecutedCommands.Count);
        Assert.Equal("SET SHOWPLAN_XML ON", harness.ExecutedCommands[0].CommandText);
        Assert.Equal("SELECT 1", harness.ExecutedCommands[1].CommandText);
        Assert.Equal("SET SHOWPLAN_XML OFF", harness.ExecutedCommands[2].CommandText);
        Assert.False(harness.ExecutedCommands[2].CancellationToken.IsCancellationRequested);

        Assert.NotNull(harness.LastConnection);
        Assert.True(harness.LastConnection.IsDisposed);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_OnTimeout_MapsToSqlAi0106AndAttemptsResetAndDisposesConnection()
    {
        var (service, harness) = CreateService();
        var target = new DatabaseTargetContext("srv1", "TestDb");
        harness.CustomReaderHandler = (_, _) => throw new TimeoutException("Execution timed out.");

        var result = await service.ExecuteShowplanAsync(target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.TimeoutCode, result.Error.Code);

        Assert.Equal(3, harness.ExecutedCommands.Count);
        Assert.Equal("SET SHOWPLAN_XML ON", harness.ExecutedCommands[0].CommandText);
        Assert.Equal("SELECT 1", harness.ExecutedCommands[1].CommandText);
        Assert.Equal("SET SHOWPLAN_XML OFF", harness.ExecutedCommands[2].CommandText);

        Assert.NotNull(harness.LastConnection);
        Assert.True(harness.LastConnection.IsDisposed);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_MissingShowplanPermission_MapsToSqlAi0104WithFixedMessage()
    {
        var (service, harness) = CreateService();
        var target = new DatabaseTargetContext("srv1", "TestDb");
        harness.CustomReaderHandler = (_, _) =>
            throw SqlExceptionFactory.Create(SqlServerErrorCode.ShowplanPermissionMissing, "SHOWPLAN permission denied in database 'ConfidentialDatabase'.");

        var result = await service.ExecuteShowplanAsync(target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Equal("Safety check failed: Permission denied: SHOWPLAN permission is missing.", result.Error.Message);
        Assert.DoesNotContain("ConfidentialDatabase", result.Error.Message);
        Assert.DoesNotContain("SHOWPLAN permission denied in database", result.Error.Message);

        Assert.NotNull(harness.LastConnection);
        Assert.True(harness.LastConnection.IsDisposed);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_MissingSelectPermission_MapsToSqlAi0104WithFixedMessage()
    {
        var (service, harness) = CreateService();
        var target = new DatabaseTargetContext("srv1", "TestDb");
        harness.CustomReaderHandler = (_, _) =>
            throw SqlExceptionFactory.Create(229, "The SELECT permission was denied on the object 'SalaryTable', database 'FinanceDb', schema 'hr'.");

        var result = await service.ExecuteShowplanAsync(target, "SELECT * FROM hr.SalaryTable", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
        Assert.Equal("Safety check failed: Permission denied: SELECT permission is missing.", result.Error.Message);
        Assert.DoesNotContain("SalaryTable", result.Error.Message);
        Assert.DoesNotContain("FinanceDb", result.Error.Message);
        Assert.DoesNotContain("hr", result.Error.Message);

        Assert.NotNull(harness.LastConnection);
        Assert.True(harness.LastConnection.IsDisposed);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_PositiveResultCheck_ZeroRows_AbortsAndDiscardsConnection()
    {
        var (service, harness) = CreateService();
        var target = new DatabaseTargetContext("srv1", "TestDb");
        harness.CustomReaderHandler = (_, _) =>
            Task.FromResult<DbDataReader>(new FakeDbDataReader([new FakeDbResultSet(["Microsoft SQL Server 2005 XML Showplan"], [])]));

        var result = await service.ExecuteShowplanAsync(target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains("Query did not produce an estimated showplan result.", result.Error.Message);

        // Connection must be discarded immediately without SET SHOWPLAN_XML OFF
        Assert.Equal(2, harness.ExecutedCommands.Count);
        Assert.Equal("SET SHOWPLAN_XML ON", harness.ExecutedCommands[0].CommandText);
        Assert.Equal("SELECT 1", harness.ExecutedCommands[1].CommandText);
        Assert.DoesNotContain(harness.ExecutedCommands, c => c.CommandText == "SET SHOWPLAN_XML OFF");

        Assert.NotNull(harness.LastConnection);
        Assert.True(harness.LastConnection.IsDisposed);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_PositiveResultCheck_MultipleRows_AbortsAndDiscardsConnection()
    {
        var (service, harness) = CreateService();
        var target = new DatabaseTargetContext("srv1", "TestDb");
        harness.CustomReaderHandler = (_, _) =>
            Task.FromResult<DbDataReader>(new FakeDbDataReader([new FakeDbResultSet(
                ["Microsoft SQL Server 2005 XML Showplan"],
                [ [SampleShowplanXml], [SampleShowplanXml] ])]));

        var result = await service.ExecuteShowplanAsync(target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains("Query did not produce an estimated showplan result.", result.Error.Message);

        Assert.Equal(2, harness.ExecutedCommands.Count);
        Assert.DoesNotContain(harness.ExecutedCommands, c => c.CommandText == "SET SHOWPLAN_XML OFF");
        Assert.NotNull(harness.LastConnection);
        Assert.True(harness.LastConnection.IsDisposed);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_PositiveResultCheck_MultipleColumns_AbortsAndDiscardsConnection()
    {
        var (service, harness) = CreateService();
        var target = new DatabaseTargetContext("srv1", "TestDb");
        harness.CustomReaderHandler = (_, _) =>
            Task.FromResult<DbDataReader>(new FakeDbDataReader([new FakeDbResultSet(
                ["PlanCol", "ExtraCol"],
                [ [SampleShowplanXml, "ExtraVal"] ])]));

        var result = await service.ExecuteShowplanAsync(target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains("Query did not produce an estimated showplan result.", result.Error.Message);

        Assert.Equal(2, harness.ExecutedCommands.Count);
        Assert.DoesNotContain(harness.ExecutedCommands, c => c.CommandText == "SET SHOWPLAN_XML OFF");
        Assert.NotNull(harness.LastConnection);
        Assert.True(harness.LastConnection.IsDisposed);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_PositiveResultCheck_MultipleResultSets_AbortsAndDiscardsConnection()
    {
        var (service, harness) = CreateService();
        var target = new DatabaseTargetContext("srv1", "TestDb");
        harness.CustomReaderHandler = (_, _) =>
            Task.FromResult<DbDataReader>(new FakeDbDataReader([
                new FakeDbResultSet(["Microsoft SQL Server 2005 XML Showplan"], [ [SampleShowplanXml] ]),
                new FakeDbResultSet(["ExtraResult"], [ ["data"] ])
            ]));

        var result = await service.ExecuteShowplanAsync(target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains("Query did not produce an estimated showplan result.", result.Error.Message);

        Assert.Equal(2, harness.ExecutedCommands.Count);
        Assert.DoesNotContain(harness.ExecutedCommands, c => c.CommandText == "SET SHOWPLAN_XML OFF");
        Assert.NotNull(harness.LastConnection);
        Assert.True(harness.LastConnection.IsDisposed);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_PositiveResultCheck_NonShowplanContent_AbortsAndDiscardsConnection()
    {
        var (service, harness) = CreateService();
        var target = new DatabaseTargetContext("srv1", "TestDb");
        harness.CustomReaderHandler = (_, _) =>
            Task.FromResult<DbDataReader>(new FakeDbDataReader([new FakeDbResultSet(
                ["Col1"],
                [ ["Regular data row content, not showplan xml"] ])]));

        var result = await service.ExecuteShowplanAsync(target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains("Query did not produce an estimated showplan result.", result.Error.Message);

        Assert.Equal(2, harness.ExecutedCommands.Count);
        Assert.DoesNotContain(harness.ExecutedCommands, c => c.CommandText == "SET SHOWPLAN_XML OFF");
        Assert.NotNull(harness.LastConnection);
        Assert.True(harness.LastConnection.IsDisposed);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_PositiveResultCheck_NullOrEmptyContent_AbortsAndDiscardsConnection()
    {
        var (service, harness) = CreateService();
        var target = new DatabaseTargetContext("srv1", "TestDb");
        harness.CustomReaderHandler = (_, _) =>
            Task.FromResult<DbDataReader>(new FakeDbDataReader([new FakeDbResultSet(
                ["Col1"],
                [ [DBNull.Value] ])]));

        var result = await service.ExecuteShowplanAsync(target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains("Query did not produce an estimated showplan result.", result.Error.Message);

        Assert.Equal(2, harness.ExecutedCommands.Count);
        Assert.DoesNotContain(harness.ExecutedCommands, c => c.CommandText == "SET SHOWPLAN_XML OFF");
        Assert.NotNull(harness.LastConnection);
        Assert.True(harness.LastConnection.IsDisposed);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_SizeCapExceeded_StopsReadingAndReturnsSqlAi0102()
    {
        var (service, harness) = CreateService(opts =>
        {
            opts.ExplainQuery.MaxShowplanXmlBytes = 64; // Very small limit to trigger size cap
        });
        var target = new DatabaseTargetContext("srv1", "TestDb");

        var result = await service.ExecuteShowplanAsync(target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
        Assert.Contains("exceeds the configured limit of 64 bytes", result.Error.Message);

        Assert.NotNull(harness.LastConnection);
        Assert.True(harness.LastConnection.IsDisposed);
    }

    [Fact]
    public async Task ExecuteShowplanAsync_PrimaryConnectionFailure_ReturnsSqlAi0105()
    {
        var (service, harness) = CreateService();
        harness.ConnectionOpenFailure = new PrimaryConnectionException(
            new SqlToAiError(SqlToAiError.InfrastructureErrorCode, "Endpoint unreachable"), false);
        var target = new DatabaseTargetContext("srv1", "TestDb");

        var result = await service.ExecuteShowplanAsync(target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InfrastructureErrorCode, result.Error.Code);
        Assert.Contains("Endpoint unreachable", result.Error.Message);
    }
}
