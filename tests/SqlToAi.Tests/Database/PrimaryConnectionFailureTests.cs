using SqlToAi.Cli;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Metadata;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

[Collection("SqlConnectionFactory")]
public sealed class PrimaryConnectionFailureTests
{
    private const string RawMarker = "Server=raw-connection-marker;User ID=synthetic-user;Password=password-marker;";

    [Theory]
    [InlineData(McpConstants.ToolListDatabases)]
    [InlineData(McpConstants.ToolSearchDatabases)]
    [InlineData(McpConstants.ToolSearchObjects)]
    [InlineData(McpConstants.ToolGetSchema)]
    [InlineData(McpConstants.ToolGetSchemaForeignKeys)]
    [InlineData(McpConstants.ToolValidateQuery)]
    [InlineData(McpConstants.ToolExecuteQuery)]
    [InlineData(McpConstants.ToolCompareQueries)]
    [InlineData(McpConstants.ToolMeasurePerformance)]
    [InlineData(McpConstants.ToolBenchmarkOptimization)]
    [InlineData(McpConstants.ToolSuggestIndexes)]
    public async Task Dispatch_TlsOpenFailure_ReturnsSafeInfrastructureErrorAndLogs(string tool)
    {
        using var harness = new FailureHarness(TlsFailure());
        var result = await harness.DispatchAsync(tool);

        Assert.True(result.IsError);
        string text = string.Join("\n", result.Content.Select(content => content.Text));
        Assert.Contains(SqlToAiError.InfrastructureErrorCode, text);
        // Migrated target-aware tools identify the owning named-server settings path;
        // not-yet-migrated tools keep the legacy SqlServer section name.
        string expectedSettingsPath = tool is McpConstants.ToolExecuteQuery
            or McpConstants.ToolValidateQuery
            or McpConstants.ToolSearchObjects
            or McpConstants.ToolGetSchema
            or McpConstants.ToolGetSchemaForeignKeys
            ? "SqlToAi:Servers"
            : "SqlServer";
        Assert.Contains(expectedSettingsPath, text);
        Assert.Contains("TLS/certificate", text);
        AssertSafe(text, harness);
    }

    [Theory]
    [InlineData(McpConstants.ToolListDatabases)]
    [InlineData(McpConstants.ToolSearchObjects)]
    [InlineData(McpConstants.ToolGetSchema)]
    [InlineData(McpConstants.ToolGetSchemaForeignKeys)]
    [InlineData(McpConstants.ToolValidateQuery)]
    [InlineData(McpConstants.ToolExecuteQuery)]
    [InlineData(McpConstants.ToolCompareQueries)]
    [InlineData(McpConstants.ToolMeasurePerformance)]
    [InlineData(McpConstants.ToolSuggestIndexes)]
    public async Task Dispatch_ActualOpenTimeout_RetainsTimeoutAndSafeLogs(string tool)
    {
        using var harness = new FailureHarness(SqlExceptionFactory.Create(-2, "Timeout during TLS handshake. " + RawMarker));
        var result = await harness.DispatchAsync(tool);

        Assert.True(result.IsError);
        string text = string.Join("\n", result.Content.Select(content => content.Text));
        Assert.Contains(SqlToAiError.TimeoutCode, text);
        AssertSafe(text, harness);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Script_TlsOpenFailure_ProtectsReportAndLoggerPayload(bool useTransaction)
    {
        using var harness = new FailureHarness(TlsFailure(), AccessLevel.ReadWrite);
        var report = await harness.Script.ExecuteAsync(new ScriptExecutionRequest(
            new SqlScriptFile("script.sql", "SELECT 1\nGO\nSELECT 2", "UTF-8"),
            TestConstants.QualifiedDatabaseName, UseTransaction: useTransaction), TestContext.Current.CancellationToken);

        Assert.Equal(SqlToAiError.InfrastructureErrorCode, report.Error!.Code);
        AssertSafe(report.ToString(), harness);
        Assert.All(report.Batches, batch => Assert.DoesNotContain("marker", batch.ToString()));
    }

    [Theory]
    [InlineData("objects")]
    [InlineData("schema")]
    [InlineData("detail")]
    public async Task Schema_TlsOpenFailure_RetainsInfrastructureContract(string operation)
    {
        using var harness = new FailureHarness(TlsFailure());
        var token = TestContext.Current.CancellationToken;
        SqlToAiError error = operation switch
        {
            "objects" => (await harness.Schema.GetExportObjectsAsync(TestConstants.QualifiedDatabaseName, token)).Error,
            "schema" => (await harness.Schema.GetSchemaAsync(TestConstants.QualifiedDatabaseName, "dbo.Table", token)).Error,
            _ => (await harness.Schema.GetSchemaIndexesAsync(TestConstants.QualifiedDatabaseName, "dbo.Table", token)).Error
        };
        Assert.Equal(SqlToAiError.InfrastructureErrorCode, error.Code);
        AssertSafe(error.Message, harness);
    }

    [Fact]
    public async Task ListDatabases_NonTlsFailure_PreservesConfiguredFallbackAndSafeLog()
    {
        using var harness = new FailureHarness(new InvalidOperationException(RawMarker));
        var result = await harness.Schema.ListDatabasesAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Contains(TestConstants.DatabaseName, result.Value);
        AssertSafe(string.Join(",", result.Value), harness);
    }

    [Fact]
    public async Task Query_Cancellation_PropagatesWithoutFailureLog()
    {
        using var harness = new FailureHarness(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => harness.DispatchAsync(McpConstants.ToolExecuteQuery));
        Assert.Empty(harness.Log.Entries);
        Assert.Equal(1, harness.Factory.OpenCount);
    }

    [Fact]
    public async Task Cli_TlsToolFailure_WritesOnlyStderrAndReturnsNonzero()
    {
        using var harness = new FailureHarness(TlsFailure());
        var result = await harness.DispatchAsync(McpConstants.ToolExecuteQuery);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        TextWriter previousOut = Console.Out;
        TextWriter previousError = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var print = typeof(Program).GetMethod("PrintToolResult", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(1, print.Invoke(null, [result]));
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
        Assert.Empty(stdout.ToString());
        Assert.Contains(SqlToAiError.InfrastructureErrorCode, stderr.ToString());
        AssertSafe(stderr.ToString(), harness);
    }

    private static Exception TlsFailure() => SqlExceptionFactory.Create(unchecked((int)0x80090325),
        "Untrusted server certificate. " + RawMarker, severity: 20,
        inner: new InvalidOperationException("inner-secret-marker " + RawMarker));

    private static void AssertSafe(string response, FailureHarness harness)
    {
        Assert.DoesNotContain("marker", response);
        Assert.NotEmpty(harness.Log.Entries);
        foreach (var (message, exception) in harness.Log.Entries)
        {
            Assert.DoesNotContain("marker", message);
            Assert.NotNull(exception);
            // "SqlServer" covers both the legacy section name and the "SqlToAi:Servers:<alias>"
            // settings path used by migrated target-aware callers.
            Assert.Contains("Server", exception.Message);
            Assert.DoesNotContain("marker", exception.ToString());
            Assert.Null(exception.InnerException);
        }
        Assert.Equal(1, harness.Factory.OpenCount);
    }

    private sealed class FailureHarness : IDisposable
    {
        private readonly ILoggerFactory _loggers;
        private readonly ToolDispatcher _dispatcher;
        internal FailureHarness(Exception failure, AccessLevel level = AccessLevel.ReadOnlyAnonymized)
        {
            Factory = new FailingOpenFactory(failure);
            _loggers = LoggerFactory.Create(builder => builder.AddProvider(Log));
            var configuration = new SqlToAiOptions().WithNamedServers(TestTargets.ServerAlias);
            configuration.Databases.ReadWrite = [TestConstants.DatabaseName];
            var options = Options.Create(configuration);
            var safety = new FakeQuerySafetyValidator(new QuerySafetyCheckResult(level, level == AccessLevel.ReadWrite));
            var security = new FakeSecurityGuard(true);
            var access = new FakeAccessLevelProvider(level);
            Schema = new SchemaService(Factory, security, access,
                new MetadataProvider(Factory, options, _loggers.CreateLogger<MetadataProvider>()),
                new AlwaysAllowPolicyResolver(), options, _loggers.CreateLogger<SchemaService>());
            var query = new QueryExecutionService(Factory, safety,
                new AnonymizationDependencies(new Anonymizer(options, new TokenVault())), options, _loggers.CreateLogger<QueryExecutionService>());
            Script = new ScriptExecutionService(Factory, safety, query, options, _loggers.CreateLogger<ScriptExecutionService>());
            var dependencies = new QueryExecutionDependencies(Factory, safety, options);
            var performance = new PerformanceMeasurementService(dependencies, _loggers.CreateLogger<PerformanceMeasurementService>());
            var comparison = new QueryComparisonService(dependencies, _loggers.CreateLogger<QueryComparisonService>());
            var analysis = new DatabaseAnalysisServices(performance, comparison,
                new OptimizationBenchmarkService(comparison, performance, _loggers.CreateLogger<OptimizationBenchmarkService>()),
                new IndexSuggestionService(Factory, security, access, options, _loggers.CreateLogger<IndexSuggestionService>()));
            _dispatcher = new ToolDispatcher(Schema, query, Script,
                new QueryValidationService(Factory, safety, options, _loggers.CreateLogger<QueryValidationService>()),
                analysis, options, _loggers.CreateLogger<ToolDispatcher>(),
                new SchemaExportService(Schema, _loggers.CreateLogger<SchemaExportService>()));
        }

        internal FailingOpenFactory Factory { get; }
        internal LogCapture Log { get; } = new();
        internal SchemaService Schema { get; }
        internal ScriptExecutionService Script { get; }
        internal Task<ToolCallResult> DispatchAsync(string tool) => _dispatcher.DispatchAsync(new ToolCallParams
        {
            Name = tool,
            Arguments = new Dictionary<string, object?>
            {
                [McpConstants.ArgDatabase] = TestConstants.QualifiedDatabaseName,
                [McpConstants.ArgQuery] = "SELECT 1",
                [McpConstants.ArgQueryA] = "SELECT 1",
                [McpConstants.ArgQueryB] = "SELECT 1",
                [McpConstants.ArgObjectName] = "dbo.Table",
                [McpConstants.ArgSearchTerm] = "Table"
            }
        }, TestContext.Current.CancellationToken);
        public void Dispose() => _loggers.Dispose();
    }

    private sealed class LogCapture : ILoggerProvider, ILogger
    {
        internal List<(string Message, Exception? Exception)> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((formatter(state, exception), exception));
        public void Dispose() { }
    }

    private sealed class FailingOpenFactory(Exception failure) : IDatabaseConnectionFactory
    {
        public System.Data.Common.DbConnection CreateTargetConnection(SqlToAi.Domain.DatabaseTargetContext target) => CreateConnection(target.DatabaseName);
        public SqlToAi.Domain.Result<System.Data.Common.DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

        internal int OpenCount { get; private set; }
        public DbConnection CreateConnection(string? databaseName = null) => new FailingConnection(this, failure);
        private sealed class FailingConnection(FailingOpenFactory owner, Exception failure) : DbConnection
        {
            [AllowNull]
            public override string ConnectionString { get; set; } = RawMarker;
            public override string Database => TestConstants.DatabaseName;
            public override string DataSource => "TestServer";
            public override string ServerVersion => "1";
            public override ConnectionState State => ConnectionState.Closed;
            public override void Open() => throw new NotSupportedException();
            public override Task OpenAsync(CancellationToken cancellationToken)
            {
                owner.OpenCount++;
                return Task.FromException(failure);
            }
            public override void Close() { }
            public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
            protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        }
    }
}
