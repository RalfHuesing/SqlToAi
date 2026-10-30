using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class DataExportServiceTests
{
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("relative/file.csv")]
    [InlineData("relative\\file.csv")]
    [InlineData("C:file.csv")]
    [InlineData("\\file.csv")]
    [InlineData(@"\\?\C:\file.csv")]
    [InlineData(@"\\.\C:\file.csv")]
    [InlineData("http://server/file.csv")]
    [InlineData("file:///C:/file.csv")]
    [InlineData("C:\\directory\\")]
    public async Task ExportDataAsync_RejectsInvalidOrRelativePaths(string invalidPath)
    {
        var (service, _) = CreateService();
        var request = new DataExportRequest("test/DemoDB", "SELECT 1", invalidPath);

        var result = await service.ExportDataAsync(request, TestCt);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task ExportDataAsync_RejectsMissingParentDirectory()
    {
        var (service, _) = CreateService();
        string nonExistentPath = @"C:\NonExistent_Dir_SqlToAi_987654321\export.csv";
        var request = new DataExportRequest("test/DemoDB", "SELECT 1", nonExistentPath);

        var result = await service.ExportDataAsync(request, TestCt);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Contains("target directory", result.Error.Message);
    }

    [Fact]
    public async Task ExportDataAsync_EnforcesOverwriteProtection()
    {
        using var temp = TestTempDirectory.Create();
        string outputPath = Path.Combine(temp.DirectoryPath, "data.csv");
        await File.WriteAllTextAsync(outputPath, "original content", TestCt);

        var rowConfig = new MockQueryRowConfig(StringValue: "new data", RowCount: 1, ColumnName: "Val");
        var (service, _) = CreateService(rowConfig: rowConfig);

        // 1. Without overwrite -> FileAlreadyExistsCode (SQL-AI-0114)
        var requestWithoutOverwrite = new DataExportRequest("test/DemoDB", "SELECT 1", outputPath, Overwrite: false);
        var failureResult = await service.ExportDataAsync(requestWithoutOverwrite, TestCt);

        Assert.True(failureResult.IsFailure);
        Assert.Equal(SqlToAiError.FileAlreadyExistsCode, failureResult.Error.Code);
        Assert.Equal("original content", await File.ReadAllTextAsync(outputPath, TestCt));

        // 2. With overwrite -> Success, replaced content
        var requestWithOverwrite = new DataExportRequest("test/DemoDB", "SELECT 1", outputPath, Overwrite: true);
        var successResult = await service.ExportDataAsync(requestWithOverwrite, TestCt);

        Assert.True(successResult.IsSuccess);
        Assert.Equal("completed", successResult.Value.Status);
        string content = await File.ReadAllTextAsync(outputPath, TestCt);
        Assert.Contains("new data", content);
    }

    [Fact]
    public async Task ExportDataAsync_CleansUpTempFileOnCancellation()
    {
        using var temp = TestTempDirectory.Create();
        string outputPath = Path.Combine(temp.DirectoryPath, "cancelled.csv");
        using var cts = new CancellationTokenSource();

        var rows = Enumerable.Range(1, 500)
            .Select(i => new object?[] { i, $"Row{i}" })
            .ToList();

        var factory = new DelegateConnectionFactory(_ =>
            new CancellingDbDataReader(["Id", "Name"], rows, cts, cancelAfterRow: 100));

        var (service, _) = CreateService(customFactory: factory);
        var request = new DataExportRequest("test/DemoDB", "SELECT Id, Name FROM LargeTable", outputPath);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportDataAsync(request, cts.Token));

        // Output file must not exist
        Assert.False(File.Exists(outputPath));

        // No .tmp files left in target directory
        var tmpFiles = Directory.GetFiles(temp.DirectoryPath, "*.tmp");
        Assert.Empty(tmpFiles);
    }

    [Fact]
    public async Task ExportDataAsync_CleansUpTempFileOnStreamingException()
    {
        using var temp = TestTempDirectory.Create();
        string outputPath = Path.Combine(temp.DirectoryPath, "failed.csv");

        var rows = Enumerable.Range(1, 500)
            .Select(i => new object?[] { i, $"Row{i}" })
            .ToList();

        var factory = new DelegateConnectionFactory(_ =>
            new FaultingDbDataReader(["Id", "Name"], rows, throwAfterRow: 50));

        var (service, _) = CreateService(customFactory: factory);
        var request = new DataExportRequest("test/DemoDB", "SELECT Id, Name FROM LargeTable", outputPath);

        var result = await service.ExportDataAsync(request, TestCt);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);

        // Output file must not exist
        Assert.False(File.Exists(outputPath));

        // No .tmp files left in target directory
        var tmpFiles = Directory.GetFiles(temp.DirectoryPath, "*.tmp");
        Assert.Empty(tmpFiles);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public async Task ExportDataAsync_RejectsMutatingStatementsAcrossAllAccessLevels(AccessLevel accessLevel)
    {
        using var temp = TestTempDirectory.Create();
        string outputPath = Path.Combine(temp.DirectoryPath, "mutation.csv");

        var (service, factory) = CreateService(accessLevel: accessLevel);
        var request = new DataExportRequest("test/DemoDB", "UPDATE Users SET Name = 'Attacker'", outputPath);

        var result = await service.ExportDataAsync(request, TestCt);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
        Assert.Contains("mutating SQL statements", result.Error.Message);
        if (accessLevel == AccessLevel.ReadWrite)
        {
            Assert.Contains("strictly read-only", result.Error.Message);
        }
        Assert.False(File.Exists(outputPath));

        // No write connection opened or committed
        if (factory?.LastConnection?.LastTransaction != null)
        {
            Assert.Equal(0, factory.LastConnection.LastTransaction.CommitCount);
        }
    }

    [Fact]
    public async Task ExportDataAsync_RejectsSchemaOnlyAccessLevel()
    {
        using var temp = TestTempDirectory.Create();
        string outputPath = Path.Combine(temp.DirectoryPath, "schema_only.csv");

        var (service, _) = CreateService(accessLevel: AccessLevel.SchemaOnly);
        var request = new DataExportRequest("test/DemoDB", "SELECT 1", outputPath);

        var result = await service.ExportDataAsync(request, TestCt);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportDataAsync_AppliesAnonymizationForAnonymizedDatabases()
    {
        using var temp = TestTempDirectory.Create();
        string outputPath = Path.Combine(temp.DirectoryPath, "anonymized.csv");

        var origin = new MockSchemaOrigin("Customers", "Email", Available: true);
        var rowConfig = new MockQueryRowConfig(
            StringValue: "secret@company.com",
            RowCount: 1,
            ColumnName: "Email",
            Origin: origin);

        var (service, _) = CreateService(accessLevel: AccessLevel.ReadOnlyAnonymized, rowConfig: rowConfig);
        var request = new DataExportRequest("test/DemoDB", "SELECT Email FROM Customers", outputPath);

        var result = await service.ExportDataAsync(request, TestCt);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WasAnonymized);
        Assert.Contains("Customers.Email", result.Value.AnonymizedColumns);

        string content = await File.ReadAllTextAsync(outputPath, TestCt);
        Assert.DoesNotContain("secret@company.com", content);
        Assert.Contains("@", content);
    }

    [Fact]
    public async Task ExportDataAsync_StreamsLargeDataSetsAndEnforcesLimits()
    {
        using var temp = TestTempDirectory.Create();
        string outputPath = Path.Combine(temp.DirectoryPath, "large.csv");

        const int totalRows = 25_000;
        var rows = Enumerable.Range(1, totalRows)
            .Select(i => new object?[] { i, $"Item_{i}" })
            .ToList();

        var factory = new DelegateConnectionFactory(_ =>
            new FakeDbDataReader(["Id", "ItemName"], rows));

        var exportOptions = new DataExportOptions
        {
            DefaultRowLimit = 10_000,
            MaxRowLimit = 20_000
        };

        var (service, _) = CreateService(customFactory: factory, exportOptions: exportOptions);

        // 1. Unspecified limit -> capped at DefaultRowLimit (10,000)
        var req1 = new DataExportRequest("test/DemoDB", "SELECT Id, ItemName FROM Items", outputPath, Overwrite: true);
        var res1 = await service.ExportDataAsync(req1, TestCt);

        Assert.True(res1.IsSuccess);
        Assert.Equal(10_000, res1.Value.RowCount);

        // 2. Requested limit 15,000 -> respected (under MaxRowLimit)
        var req2 = new DataExportRequest("test/DemoDB", "SELECT Id, ItemName FROM Items", outputPath, Overwrite: true, RequestedRowLimit: 15_000);
        var res2 = await service.ExportDataAsync(req2, TestCt);

        Assert.True(res2.IsSuccess);
        Assert.Equal(15_000, res2.Value.RowCount);

        // 3. Requested limit 50,000 -> capped at MaxRowLimit (20,000)
        var req3 = new DataExportRequest("test/DemoDB", "SELECT Id, ItemName FROM Items", outputPath, Overwrite: true, RequestedRowLimit: 50_000);
        var res3 = await service.ExportDataAsync(req3, TestCt);

        Assert.True(res3.IsSuccess);
        Assert.Equal(20_000, res3.Value.RowCount);

        // 4. Invalid limit <= 0 -> rejected
        var req4 = new DataExportRequest("test/DemoDB", "SELECT Id, ItemName FROM Items", outputPath, Overwrite: true, RequestedRowLimit: 0);
        var res4 = await service.ExportDataAsync(req4, TestCt);

        Assert.True(res4.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, res4.Error.Code);
    }

    [Fact]
    public async Task ExportDataAsync_RejectsQueriesWithNoResultSet()
    {
        using var temp = TestTempDirectory.Create();
        string outputPath = Path.Combine(temp.DirectoryPath, "no_result.csv");

        var factory = new DelegateConnectionFactory(_ =>
            new FakeDbDataReader(resultSets: [new FakeDbResultSet([], [])]));

        var (service, _) = CreateService(customFactory: factory);
        var request = new DataExportRequest("test/DemoDB", "PRINT 'Hello'", outputPath);

        var result = await service.ExportDataAsync(request, TestCt);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
        Assert.Contains("did not return a result set", result.Error.Message);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportDataAsync_ColumnIndependentQuery_DoesNotAnonymizeLiteralValues()
    {
        using var temp = TestTempDirectory.Create();
        string outputPath = Path.Combine(temp.DirectoryPath, "literal.csv");

        var rows = new List<object?[]> { new object?[] { "literal_secret" } };
        var factory = new DelegateConnectionFactory(_ =>
            new FakeDbDataReader(["Val"], rows));

        var (service, _) = CreateService(
            accessLevel: AccessLevel.ReadOnlyAnonymized,
            customFactory: factory);

        // Query with only literals has no column references, so SqlColumnReferenceDetector.IsColumnIndependent is true
        var request = new DataExportRequest("test/DemoDB", "SELECT 'literal_secret' AS Val", outputPath);

        var result = await service.ExportDataAsync(request, TestCt);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.WasAnonymized);
        Assert.Empty(result.Value.AnonymizedColumns);

        string content = await File.ReadAllTextAsync(outputPath, TestCt);
        Assert.Contains("literal_secret", content);
    }

    [Fact]
    public async Task ExportDataAsync_ResetsRowCountOnFailure()
    {
        using var temp = TestTempDirectory.Create();
        string outputPath = Path.Combine(temp.DirectoryPath, "fail.csv");

        var factory = new DelegateConnectionFactory(_ =>
            new FaultingDbDataReader(["Id"], [[1]], throwAfterRow: 0));

        var (service, _) = CreateService(customFactory: factory);
        var request = new DataExportRequest("test/DemoDB", "SELECT Id FROM FaultingTable", outputPath);

        var result = await service.ExportDataAsync(request, TestCt);

        Assert.True(result.IsFailure);
        Assert.Contains("SET ROWCOUNT 0", factory.ExecutedCommands);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportDataAsync_AdvancesPastLeadingEmptyResultSets()
    {
        using var temp = TestTempDirectory.Create();
        string outputPath = Path.Combine(temp.DirectoryPath, "multi_result.csv");

        var emptyResultSet = new FakeDbResultSet([], []);
        var dataResultSet = new FakeDbResultSet(["Id", "Name"], [[101, "Alice"], [102, "Bob"]]);

        var factory = new DelegateConnectionFactory(_ =>
            new FakeDbDataReader(resultSets: [emptyResultSet, dataResultSet]));

        var (service, _) = CreateService(customFactory: factory);
        var request = new DataExportRequest("test/DemoDB", "SELECT Id, Name FROM Users", outputPath);

        var result = await service.ExportDataAsync(request, TestCt);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.RowCount);
        Assert.Equal("completed", result.Value.Status);

        string content = await File.ReadAllTextAsync(outputPath, TestCt);
        Assert.Contains("Id,Name", content);
        Assert.Contains("101,Alice", content);
        Assert.Contains("102,Bob", content);
    }

    private static (DataExportService Service, MockQueryConnectionFactory? Factory) CreateService(
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        MockQueryRowConfig? rowConfig = null,
        DataExportOptions? exportOptions = null,
        bool isAllowed = true,
        IAnonymizer? anonymizer = null,
        IAnonymizationRuleProvider? ruleProvider = null,
        IQueryTokenResolver? tokenResolver = null,
        IDatabaseConnectionFactory? customFactory = null)
    {
        var options = new SqlToAiOptions();
        options.Servers["test"] = new NamedServerOptions { Server = "localhost" };
        if (exportOptions != null)
        {
            options.QueryExecution.Export = exportOptions;
        }

        MockQueryConnectionFactory? mockFactory = null;
        IDatabaseConnectionFactory connectionFactory;
        if (customFactory != null)
        {
            connectionFactory = customFactory;
        }
        else
        {
            mockFactory = new MockQueryConnectionFactory(rowConfig);
            connectionFactory = mockFactory;
        }

        var securityGuard = new FakeSecurityGuard(isAllowed);
        var accessLevelProvider = new FakeAccessLevelProvider(accessLevel);
        var readOnlyGuard = new ReadOnlyGuard();
        var querySafetyValidator = new QuerySafetyValidator(securityGuard, accessLevelProvider, readOnlyGuard);

        anonymizer ??= new Anonymizer(Options.Create(options), new TokenVault());
        var anonymizationDeps = new AnonymizationDependencies(anonymizer, ruleProvider, tokenResolver);

        var service = new DataExportService(
            connectionFactory,
            querySafetyValidator,
            readOnlyGuard,
            anonymizationDeps,
            Options.Create(options),
            new CapturingLogger<DataExportService>());

        return (service, mockFactory);
    }

    private sealed class DelegateConnectionFactory(Func<DatabaseTargetContext, DbDataReader> readerFactory) : IDatabaseConnectionFactory
    {
        public FakeDbConnection? LastConnection { get; private set; }
        public List<string> ExecutedCommands { get; } = [];

        public DbConnection CreateTargetConnection(DatabaseTargetContext target)
        {
            LastConnection = new FakeDbConnection(
                conn => new FakeDbCommand(conn, new FakeDbCommandHandlers(
                    ExecuteScalar: _ => 1,
                    ExecuteReader: cmd =>
                    {
                        ExecutedCommands.Add(cmd.CommandText);
                        return readerFactory(target);
                    },
                    ExecuteNonQuery: cmd =>
                    {
                        ExecutedCommands.Add(cmd.CommandText);
                        return 0;
                    })),
                new FakeDbConnectionOptions(
                    Database: target.DatabaseName,
                    DataSource: "mock",
                    ServerVersion: "16.0",
                    BeginTransaction: (conn, _) => new FakeDbTransaction(conn)));
            return LastConnection;
        }

        public DbConnection CreateConnection(string? databaseName) => throw new NotSupportedException();
        public DbConnection CreateConnection() => throw new NotSupportedException();
        public Result<DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();
    }

    private sealed class CancellingDbDataReader(
        string[] columns,
        IReadOnlyList<object?[]> rows,
        CancellationTokenSource cts,
        int cancelAfterRow) : FakeDbDataReader(columns, rows)
    {
        private int _readCount;

        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            if (_readCount >= cancelAfterRow)
            {
                await cts.CancelAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            bool result = await base.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (result)
            {
                _readCount++;
            }
            return result;
        }
    }

    private sealed class FaultingDbDataReader(
        string[] columns,
        IReadOnlyList<object?[]> rows,
        int throwAfterRow) : FakeDbDataReader(columns, rows)
    {
        private int _readCount;

        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            if (_readCount >= throwAfterRow)
            {
                throw new InvalidOperationException("Simulated unexpected failure during streaming.");
            }
            bool result = await base.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (result)
            {
                _readCount++;
            }
            return result;
        }
    }
}
