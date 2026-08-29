#nullable enable

using System.Text;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class ScriptExecutionServiceIntegrationTests
{
    private const string MarkerPrefix = "sql_file_execution_step_008_";
    private const string ScriptFilePrefix = "SqlToAiScriptExecutionTests_";

    private readonly SqlServerFixture _fixture;
    private readonly string _databaseName;

    public ScriptExecutionServiceIntegrationTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
        _databaseName = TestConstants.DatabaseName;
    }

    [Fact]
    public async Task ExecuteAsync_MultipleGoBatchesWithRepeat_ReturnsOrderedResultsAndMetrics()
    {
        string scriptPath = CreateScriptPath();
        try
        {
            SqlScriptFile scriptFile = ReadScript(scriptPath, "SELECT CAST(1 AS int) AS BatchValue\nGO 2\nSELECT CAST(2 AS int) AS BatchValue\nGO\nSELECT CAST(3 AS int) AS BatchValue");
            ScriptExecutionReport report = await BuildService(_fixture.QuerySafetyValidator).ExecuteAsync(
                new ScriptExecutionRequest(scriptFile, _databaseName),
                TestContext.Current.CancellationToken);

            Assert.Equal(ScriptExecutionStatus.Success, report.Status);
            Assert.Equal(ScriptTransactionMode.ReadWriteAtomic, report.Mode);
            Assert.Equal(3, report.Batches.Count);
            Assert.All(report.Batches, batch => Assert.Equal(ScriptBatchStatus.Success, batch.Status));
            Assert.Equal(2, report.Batches[0].Batch.RepeatCount);
            Assert.Equal(2, report.Batches[0].Executions.Count);
            Assert.Contains("\"BatchValue\":1", report.Batches[0].Executions[0].Data, StringComparison.Ordinal);
            Assert.Contains("\"BatchValue\":2", report.Batches[1].Executions[0].Data, StringComparison.Ordinal);
            Assert.Contains("\"BatchValue\":3", report.Batches[2].Executions[0].Data, StringComparison.Ordinal);
            Assert.True(report.ElapsedMs >= 0);
            Assert.True(report.CpuTimeMs >= 0);
            Assert.True(report.LogicalReads >= 0);

            string markdown = ScriptExecutionReportRenderer.Render(report);
            Assert.Contains("ReadWriteAtomic", markdown, StringComparison.Ordinal);
            Assert.Contains("elapsed_ms", markdown, StringComparison.Ordinal);
            Assert.Contains("### Batch 3", markdown, StringComparison.Ordinal);
        }
        finally
        {
            DeleteScriptFile(scriptPath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReadWriteAtomicFailure_RollsBackAndRendersDiagnostics()
    {
        string marker = CreateMarker();
        string scriptPath = CreateScriptPath();
        try
        {
            string script = $"INSERT INTO dbo.FakeProjects (ProjectName, Mandant, Description, Status) VALUES (N'{marker}', 1, N'Step 008 atomic marker', N'Active')\nGO\nSELECT FROM dbo.FakeProjects\nGO\nSELECT 3 AS AfterFailure";
            SqlScriptFile scriptFile = ReadScript(scriptPath, script);
            ScriptExecutionReport report = await BuildService(_fixture.QuerySafetyValidator).ExecuteAsync(
                new ScriptExecutionRequest(scriptFile, _databaseName),
                TestContext.Current.CancellationToken);

            Assert.Equal(ScriptExecutionStatus.Failed, report.Status);
            Assert.Equal(ScriptTransactionMode.ReadWriteAtomic, report.Mode);
            Assert.Equal(3, report.Batches.Count);
            Assert.Equal(ScriptBatchStatus.Success, report.Batches[0].Status);
            Assert.Equal(ScriptBatchStatus.Failed, report.Batches[1].Status);
            Assert.Equal(ScriptBatchStatus.NotExecuted, report.Batches[2].Status);
            Assert.Equal(SqlToAiError.QueryErrorCode, report.Batches[1].Error!.Code);
            Assert.Equal(0, await CountMarkerAsync(marker));

            string markdown = ScriptExecutionReportRenderer.Render(report);
            Assert.Contains("failed_batch: 2", markdown, StringComparison.Ordinal);
            Assert.Contains("failed_source_lines: 3-3", markdown, StringComparison.Ordinal);
            Assert.Contains("SELECT FROM dbo.FakeProjects", markdown, StringComparison.Ordinal);
            Assert.Contains(SqlToAiError.QueryErrorCode, markdown, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupAsync(scriptPath, marker);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReadWriteProviderAutocommitFailure_PreservesEarlierCommit()
    {
        string marker = CreateMarker();
        string scriptPath = CreateScriptPath();
        try
        {
            string script = $"INSERT INTO dbo.FakeProjects (ProjectName, Mandant, Description, Status) VALUES (N'{marker}', 1, N'Step 008 autocommit marker', N'Active')\nGO\nSELECT FROM dbo.FakeProjects\nGO\nSELECT 3 AS AfterFailure";
            SqlScriptFile scriptFile = ReadScript(scriptPath, script);
            ScriptExecutionReport report = await BuildService(_fixture.QuerySafetyValidator).ExecuteAsync(
                new ScriptExecutionRequest(scriptFile, _databaseName, UseTransaction: false),
                TestContext.Current.CancellationToken);

            Assert.Equal(ScriptExecutionStatus.Failed, report.Status);
            Assert.Equal(ScriptTransactionMode.ReadWriteProviderAutocommit, report.Mode);
            Assert.Equal(ScriptBatchStatus.Success, report.Batches[0].Status);
            Assert.Equal(ScriptBatchStatus.Failed, report.Batches[1].Status);
            Assert.Equal(ScriptBatchStatus.NotExecuted, report.Batches[2].Status);
            Assert.Equal(1, await CountMarkerAsync(marker));
        }
        finally
        {
            await CleanupAsync(scriptPath, marker);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReadOnlyMutation_IsRejectedBeforeCreatingMarker()
    {
        string marker = CreateMarker();
        string scriptPath = CreateScriptPath();
        try
        {
            string script = $"INSERT INTO dbo.FakeProjects (ProjectName, Mandant, Description, Status) VALUES (N'{marker}', 1, N'Step 008 read-only marker', N'Active')";
            SqlScriptFile scriptFile = ReadScript(scriptPath, script);
            var service = BuildService(BuildReadOnlyValidator());

            ScriptExecutionReport report = await service.ExecuteAsync(
                new ScriptExecutionRequest(scriptFile, _databaseName),
                TestContext.Current.CancellationToken);

            Assert.Equal(ScriptExecutionStatus.Failed, report.Status);
            Assert.Equal(ScriptTransactionMode.NotStarted, report.Mode);
            Assert.Equal(SqlToAiError.WriteOperationBlockedCode, report.Error!.Code);
            Assert.Equal(ScriptBatchStatus.Failed, report.Batches[0].Status);
            Assert.Equal(0, await CountMarkerAsync(marker));
        }
        finally
        {
            await CleanupAsync(scriptPath, marker);
        }
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly, false)]
    [InlineData(AccessLevel.ReadOnlyAnonymized, true)]
    public async Task ExecuteAsync_ReadOnlyModes_SelectContactsWithExpectedProtection(
        AccessLevel accessLevel,
        bool expectedAnonymized)
    {
        string scriptPath = CreateScriptPath();
        try
        {
            SqlScriptFile scriptFile = ReadScript(scriptPath, "SELECT TOP 1 Name, Email, Ausfuehrer FROM dbo.FakeContacts");
            ScriptExecutionReport report = await BuildService(BuildAccessValidator(accessLevel)).ExecuteAsync(
                new ScriptExecutionRequest(scriptFile, _databaseName),
                TestContext.Current.CancellationToken);
            ScriptTransactionMode expectedMode = accessLevel == AccessLevel.ReadOnlyAnonymized
                ? ScriptTransactionMode.ReadOnlyAnonymizedRollback
                : ScriptTransactionMode.ReadOnlyRollback;

            Assert.Equal(ScriptExecutionStatus.Success, report.Status);
            Assert.Equal(expectedMode, report.Mode);
            QueryExecutionResult execution = Assert.Single(Assert.Single(report.Batches).Executions);
            Assert.NotEqual("[]", execution.Data);
            Assert.Equal(expectedAnonymized, execution.WasAnonymized);
            if (expectedAnonymized)
            {
                Assert.NotEmpty(execution.AnonymizedColumns);
                Assert.Contains("anonymized: true", ScriptExecutionReportRenderer.Render(report), StringComparison.Ordinal);
            }
            else
            {
                Assert.Empty(execution.AnonymizedColumns);
            }
        }
        finally
        {
            DeleteScriptFile(scriptPath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CreateViewAndProcedure_ExecutesSuccessfullyAndDrops()
    {
        string viewName = "dbo._SqlToAi_Test_View_" + Guid.NewGuid().ToString("N");
        string procName = "dbo._SqlToAi_Test_Proc_" + Guid.NewGuid().ToString("N");
        string scriptPath = CreateScriptPath();
        try
        {
            string script = $"CREATE VIEW {viewName} AS SELECT 42 AS Value;\nGO\nCREATE PROCEDURE {procName} AS\nBEGIN\n    SET NOCOUNT ON;\n    SELECT Value * 2 AS Doubled FROM {viewName};\nEND;\nGO\nEXEC {procName};\nGO\nDROP PROCEDURE {procName};\nGO\nDROP VIEW {viewName};";
            SqlScriptFile scriptFile = ReadScript(scriptPath, script);
            ScriptExecutionReport report = await BuildService(_fixture.QuerySafetyValidator).ExecuteAsync(
                new ScriptExecutionRequest(scriptFile, _databaseName),
                TestContext.Current.CancellationToken);

            Assert.Equal(ScriptExecutionStatus.Success, report.Status);
            Assert.Equal(5, report.Batches.Count);
            Assert.All(report.Batches, b => Assert.Equal(ScriptBatchStatus.Success, b.Status));
            Assert.Contains("\"Doubled\":84", report.Batches[2].Executions[0].Data, StringComparison.Ordinal);
        }
        finally
        {
            DeleteScriptFile(scriptPath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UseStatement_IsRejectedBeforeExecution()
    {
        string scriptPath = CreateScriptPath();
        try
        {
            string script = "USE master\nGO\nSELECT 1";
            SqlScriptFile scriptFile = ReadScript(scriptPath, script);
            ScriptExecutionReport report = await BuildService(_fixture.QuerySafetyValidator).ExecuteAsync(
                new ScriptExecutionRequest(scriptFile, _databaseName),
                TestContext.Current.CancellationToken);

            Assert.Equal(ScriptExecutionStatus.Failed, report.Status);
            Assert.Equal(SqlToAiError.SafetyCheckFailedCode, report.Error!.Code);
            Assert.Equal(ScriptTransactionMode.NotStarted, report.Mode);
        }
        finally
        {
            DeleteScriptFile(scriptPath);
        }
    }

    private ScriptExecutionService BuildService(IQuerySafetyValidator safetyValidator)
    {
        return new ScriptExecutionService(
            _fixture.ConnectionFactory,
            safetyValidator,
            _fixture.QueryExecutionService,
            Microsoft.Extensions.Options.Options.Create(_fixture.Options),
            NullLogger<ScriptExecutionService>.Instance);
    }

    private QuerySafetyValidator BuildReadOnlyValidator() => BuildAccessValidator(AccessLevel.ReadOnly);

    private QuerySafetyValidator BuildAccessValidator(AccessLevel accessLevel)
    {
        return new QuerySafetyValidator(
            _fixture.SecurityGuard,
            new FakeAccessLevelProvider(accessLevel),
            _fixture.ReadOnlyGuard);
    }

    private SqlScriptFile ReadScript(string scriptPath, string script)
    {
        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Result<SqlScriptFile> result = SqlScriptFileReader.Read(scriptPath, _fixture.Options.QueryExecution);
        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        return result.Value;
    }

    private async Task<long> CountMarkerAsync(string marker)
    {
        using var connection = _fixture.ConnectionFactory.CreateConnection(_databaseName);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.FakeProjects WHERE ProjectName = @Marker",
            new { Marker = marker },
            cancellationToken: TestContext.Current.CancellationToken));
    }

    private async Task DeleteMarkerAsync(string marker)
    {
        using var connection = _fixture.ConnectionFactory.CreateConnection(_databaseName);
        await connection.OpenAsync(CancellationToken.None);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.FakeProjects WHERE ProjectName = @Marker",
            new { Marker = marker },
            cancellationToken: CancellationToken.None));
    }

    private async Task CleanupAsync(string scriptPath, string marker)
    {
        try
        {
            await DeleteMarkerAsync(marker);
        }
        finally
        {
            DeleteScriptFile(scriptPath);
        }
    }

    private static string CreateMarker() => MarkerPrefix + Guid.NewGuid().ToString("N");

    private static string CreateScriptPath() => Path.Combine(
        Path.GetTempPath(),
        ScriptFilePrefix + Guid.NewGuid().ToString("N") + ".sql");

    private static void DeleteScriptFile(string scriptPath)
    {
        if (File.Exists(scriptPath))
        {
            File.Delete(scriptPath);
        }
    }
}
