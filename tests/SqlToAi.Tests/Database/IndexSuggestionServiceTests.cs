#nullable enable

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for <see cref="IndexSuggestionService"/>, exercising parameter
/// validation, security guards, Markdown rendering for the happy path, graceful
/// degradation on the <c>VIEW SERVER STATE</c> permission error, the
/// TD-003-refactored <see cref="PerformanceMeasurementService.IsPermissionError"/>
/// helper, and the args-record defaults. DMV rows are fed through a
/// in-memory <see cref="DbDataReader"/> supplied by a fake connection factory
/// — no real SQL Server is required.
/// </summary>
public sealed class IndexSuggestionServiceTests
{
    // -------------------------------------------------------------------------
    // Builders
    // -------------------------------------------------------------------------

    private static IndexSuggestionService BuildService(
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        Exception? throwOnExecuteReader = null,
        IReadOnlyList<DmvRow>? rows = null)
    {
        var factory = new DmvMockConnectionFactory(rows ?? [], throwOnExecuteReader);
        var options = new SqlToAiOptions();
        return new IndexSuggestionService(
            factory,
            new FakeSecurityGuard(isAllowed),
            new FakeAccessLevelProvider(accessLevel),
            Options.Create(options),
            NullLogger<IndexSuggestionService>.Instance);
    }

    private static IndexSuggestionService BuildService(DmvMockConnectionFactory factory)
    {
        var options = new SqlToAiOptions();
        return new IndexSuggestionService(
            factory,
            new FakeSecurityGuard(true),
            new FakeAccessLevelProvider(AccessLevel.ReadOnly),
            Options.Create(options),
            NullLogger<IndexSuggestionService>.Instance);
    }

    // -------------------------------------------------------------------------
    // Tests 1-3: input validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SuggestIndexesAsync_DatabaseNameEmpty_ReturnsInvalidParametersError()
    {
        var service = BuildService();
        var result = await service.SuggestIndexesAsync(new IndexSuggestionArgs(""), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task SuggestIndexesAsync_TopZero_ReturnsInvalidParametersError()
    {
        var service = BuildService();
        var result = await service.SuggestIndexesAsync(new IndexSuggestionArgs("DemoDB", Top: 0), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task SuggestIndexesAsync_MinScoreNegative_ReturnsInvalidParametersError()
    {
        var service = BuildService();
        var result = await service.SuggestIndexesAsync(
            new IndexSuggestionArgs("DemoDB", MinScore: -1.0), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Tests 4-5: security checks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SuggestIndexesAsync_DatabaseNotInWhitelist_ReturnsSafetyCheckFailedError()
    {
        var service = BuildService(isAllowed: false);
        var result = await service.SuggestIndexesAsync(new IndexSuggestionArgs("BlockedDb"), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Fact]
    public async Task SuggestIndexesAsync_DatabaseAccessLevelNone_ReturnsSafetyCheckFailedError()
    {
        var service = BuildService(accessLevel: AccessLevel.None);
        var result = await service.SuggestIndexesAsync(new IndexSuggestionArgs("DemoDB"), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Test 6: happy path - renders Markdown with score, columns, last-seek, restart hint
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SuggestIndexesAsync_QueryReturnsRows_RendersMarkdownWithScoreAndRestartHint()
    {
        var rows = new List<DmvRow>
        {
            new(
                Statement: "[dbo].[Orders]",
                IndexHandle: 1,
                UserSeeks: 45230,
                UserScans: 12,
                LastUserSeek: new DateTime(2026, 8, 3, 14, 32, 11),
                AvgTotalUserCost: 10.5,
                AvgUserImpact: 25.0,
                Columns:
                [
                    new DmvColumn(2, "EQUALITY"),
                    new DmvColumn(3, "EQUALITY"),
                    new DmvColumn(4, "INEQUALITY"),
                    new DmvColumn(5, "INCLUDE"),
                    new DmvColumn(6, "INCLUDE"),
                ]),
        };

        var service = BuildService(rows: rows);
        var result = await service.SuggestIndexesAsync(new IndexSuggestionArgs("DemoDB"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        string md = result.Value;
        Assert.Contains("# Missing Index Recommendations — DemoDB", md);
        Assert.Contains("since the last SQL Server restart", md);
        Assert.Contains("| Score |", md);
        Assert.Contains("| Table |", md);
        Assert.Contains("| Equality Columns |", md);
        Assert.Contains("| Inequality Columns |", md);
        Assert.Contains("| Include Columns |", md);
        Assert.Contains("| Seeks |", md);
        Assert.Contains("| Scans |", md);
        Assert.Contains("| Last Seek |", md);
        // 45230 * 10.5 * 25 = 11876_250.0 / 10.5 = actually score is 10.5 * 25 * 45242 = 11876025
        // The exact value is 10.5 * 25 * 45242 = 11876025
        Assert.Contains("11876025", md);
        Assert.Contains("[dbo].[Orders]", md);
        Assert.Contains("2, 3", md);
        Assert.Contains("4", md);
        Assert.Contains("5, 6", md);
        Assert.Contains("45230", md);
        Assert.Contains("12", md);
        Assert.Contains("2026-08-03", md);
    }

    // -------------------------------------------------------------------------
    // Test 7: table_name filter - passed as LIKE parameter
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SuggestIndexesAsync_TableNameFilter_PassedAsLikeParameter()
    {
        var factory = new DmvMockConnectionFactory([], throwOnExecuteReader: null);
        var service = BuildService(factory: factory);

        await service.SuggestIndexesAsync(
            new IndexSuggestionArgs("DemoDB", TableName: "Orders"), TestContext.Current.CancellationToken);

        var paramsCmd = factory.LastReaderCommand;
        Assert.NotNull(paramsCmd);
        DbParameter? tableParam = null;
        foreach (DbParameter p in paramsCmd!.Parameters)
        {
            // Dapper sometimes strips the @ prefix, sometimes keeps it. Match either.
            string name = p.ParameterName.TrimStart('@');
            if (string.Equals(name, "TableName", StringComparison.OrdinalIgnoreCase))
            {
                tableParam = p;
                break;
            }
        }

        Assert.NotNull(tableParam);
        Assert.Equal("Orders", (string)tableParam!.Value!);
    }

    // -------------------------------------------------------------------------
    // Test 8: top filter - passed as FetchNext parameter
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SuggestIndexesAsync_TopFilter_PassedAsFetchNextParameter()
    {
        var factory = new DmvMockConnectionFactory([], throwOnExecuteReader: null);
        var service = BuildService(factory: factory);

        await service.SuggestIndexesAsync(new IndexSuggestionArgs("DemoDB", Top: 25), TestContext.Current.CancellationToken);

        var paramsCmd = factory.LastReaderCommand;
        Assert.NotNull(paramsCmd);
        DbParameter? topParam = null;
        foreach (DbParameter p in paramsCmd!.Parameters)
        {
            string name = p.ParameterName.TrimStart('@');
            if (string.Equals(name, "Top", StringComparison.OrdinalIgnoreCase))
            {
                topParam = p;
                break;
            }
        }

        Assert.NotNull(topParam);
        Assert.Equal(25, (int)topParam!.Value!);
    }

    // -------------------------------------------------------------------------
    // Test 9: graceful degradation on VIEW SERVER STATE permission error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SuggestIndexesAsync_PermissionDeniedSqlException_ReturnsGracefulDegradationNote()
    {
        // SqlException has no public constructor; build it via the internal CreateInstance helper.
        var sqlEx = CreateSqlException(number: 300, message: "The user does not have permission to perform this action. VIEW SERVER STATE permission is required.");
        var service = BuildService(throwOnExecuteReader: sqlEx);

        var result = await service.SuggestIndexesAsync(new IndexSuggestionArgs("DemoDB"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        string md = result.Value;
        Assert.Contains("# Missing Index Recommendations — DemoDB", md);
        Assert.Contains("since the last SQL Server restart", md);
        Assert.Contains("VIEW SERVER STATE", md);
    }

    // -------------------------------------------------------------------------
    // Test 10: generic SqlException - returns QueryError
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SuggestIndexesAsync_GenericSqlException_ReturnsQueryError()
    {
        var sqlEx = CreateSqlException(number: 102, message: "Incorrect syntax near 'SELECTX'.");
        var service = BuildService(throwOnExecuteReader: sqlEx);

        var result = await service.SuggestIndexesAsync(new IndexSuggestionArgs("DemoDB"), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.QueryErrorCode, result.Error.Code);
    }

    // -------------------------------------------------------------------------
    // Test 11: TD-003 — IsPermissionError refactored to helper still recognizes SHOWPLAN
    // -------------------------------------------------------------------------

    [Fact]
    public void PerformanceMeasurementService_IsPermissionError_RefactoredToHelper_StillRecognizesShowplanError()
    {
        var byNumber = CreateSqlException(number: 262, message: "Some unrelated message");
        var byMessage = CreateSqlException(number: 9999, message: "VIEW permission denied to SHOWPLAN object");
        var unrelated = CreateSqlException(number: 102, message: "Incorrect syntax");

        Assert.True(PerformanceMeasurementService.IsPermissionError(byNumber, 262, "SHOWPLAN"));
        Assert.True(PerformanceMeasurementService.IsPermissionError(byMessage, 262, "SHOWPLAN"));
        Assert.False(PerformanceMeasurementService.IsPermissionError(unrelated, 262, "SHOWPLAN"));

        // And the new VIEW SERVER STATE use case works the same way
        var vssByMessage = CreateSqlException(number: 5000, message: "Insufficient permission for VIEW SERVER STATE");
        Assert.True(PerformanceMeasurementService.IsPermissionError(vssByMessage, 300, "VIEW SERVER STATE"));
    }

    // -------------------------------------------------------------------------
    // Test 12: IndexSuggestionArgs defaults
    // -------------------------------------------------------------------------

    [Fact]
    public void SuggestIndexesArgs_DefaultsAreCorrect()
    {
        var args = new IndexSuggestionArgs("FooDb");

        Assert.Equal("FooDb", args.DatabaseName);
        Assert.Null(args.TableName);
        Assert.Null(args.MinScore);
        Assert.Equal(10, args.Top);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="SqlException"/> via the internal constructor without requiring
    /// the full ADO.NET plumbing. <c>SqlException</c> has no public constructor, so we use
    /// the internal <c>(string, SqlErrorCollection)</c> constructor plus reflection-built
    /// <c>SqlError</c>/<c>SqlErrorCollection</c> instances — the established escape hatch
    /// when unit-testing permission-error code paths against the real exception type.
    /// </summary>
    private static SqlException CreateSqlException(int number, string message)
    {
        const System.Reflection.BindingFlags nonPublicInstance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        // Build an empty SqlErrorCollection via the parameterless internal constructor.
        var errorsCtor = typeof(SqlErrorCollection).GetConstructor(nonPublicInstance, binder: null, types: Type.EmptyTypes, modifiers: null)
            ?? throw new InvalidOperationException("SqlErrorCollection parameterless constructor not found.");
        var errorCollection = (SqlErrorCollection)errorsCtor.Invoke(Array.Empty<object>());

        // Build a single SqlError with the given number/message via the internal constructor.
        // Constructor signature (Microsoft.Data.SqlClient 7.0): SqlError(int infoNumber, byte errorState,
        //     byte errorClass, string server, string errorMessage, string procedure, int lineNumber,
        //     Exception exception)
        var errorCtor = typeof(SqlError).GetConstructor(
            nonPublicInstance,
            binder: null,
            types: [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception)],
            modifiers: null)
            ?? throw new InvalidOperationException("SqlError internal constructor not found.");
        var error = (SqlError)errorCtor.Invoke([number, (byte)0, (byte)16, "TestServer", message, "TestProc", 1, null!]);

        // Add the error to the collection (Add is internal on SqlErrorCollection).
        var addMethod = typeof(SqlErrorCollection).GetMethod("Add", nonPublicInstance)
            ?? throw new InvalidOperationException("SqlErrorCollection.Add not found.");
        addMethod.Invoke(errorCollection, [error]);

        // Finally, instantiate SqlException via its internal (string, SqlErrorCollection, Exception, Guid) constructor.
        var sqlExceptionCtor = typeof(SqlException).GetConstructor(
            nonPublicInstance,
            binder: null,
            types: [typeof(string), typeof(SqlErrorCollection), typeof(Exception), typeof(Guid)],
            modifiers: null)
            ?? throw new InvalidOperationException("SqlException internal constructor not found.");
        return (SqlException)sqlExceptionCtor.Invoke([message, errorCollection, null!, Guid.Empty]);
    }

    // -------------------------------------------------------------------------
    // Fake DB plumbing
    // -------------------------------------------------------------------------

    private sealed record DmvColumn(int ColumnId, string ColumnUsage);

    private sealed record DmvRow(
        string Statement,
        long IndexHandle,
        long UserSeeks,
        long UserScans,
        DateTime? LastUserSeek,
        double AvgTotalUserCost,
        double AvgUserImpact,
        IReadOnlyList<DmvColumn> Columns);

    /// <summary>
    /// A <see cref="DbConnection"/> fake that returns the given DMV rows from a single
    /// reader. If <see cref="_throwOnExecuteReader"/> is set, it is thrown on
    /// <c>ExecuteReaderAsync</c> to simulate server-side failures (e.g. permission errors).
    /// </summary>
    private sealed class DmvMockConnectionFactory(
        IReadOnlyList<DmvRow> rows,
        Exception? throwOnExecuteReader) : IDatabaseConnectionFactory
    {
        public FakeDbConnection? LastConnection { get; private set; }

        /// <summary>The most recent <see cref="FakeDbCommand"/> passed to <c>ExecuteReader</c> —
        /// lets tests inspect the bound parameters (Dapper prefixes the names with <c>@</c>, but
        /// tests strip that prefix when looking up by property name).</summary>
        public FakeDbCommand? LastReaderCommand { get; private set; }

        public DbConnection CreateConnection(string? databaseName)
        {
            var conn = new FakeDbConnection(
                c => new FakeDbCommand(
                    c,
                    new FakeDbCommandHandlers(
                        ExecuteReader: cmd => ExecuteReader(cmd, c))),
                new FakeDbConnectionOptions(
                    Database: TestConstants.DatabaseName,
                    DataSource: "mock",
                    ServerVersion: "16.0",
                    BeginTransaction: (connection, _) => new FakeDbTransaction(connection)));
            conn.LastCommand = null;
            LastConnection = conn;
            return conn;
        }

        public DbConnection CreateConnection() => CreateConnection(null);

        private FakeDbDataReader ExecuteReader(FakeDbCommand cmd, FakeDbConnection conn)
        {
            LastReaderCommand = cmd;
            if (throwOnExecuteReader != null)
            {
                throw throwOnExecuteReader;
            }

            string[] columns =
            [
                "Statement", "IndexHandle", "UserSeeks", "UserScans", "LastUserSeek",
                "AvgTotalUserCost", "AvgUserImpact", "ColumnId", "ColumnUsage",
            ];
            var raw = new List<object?[]>();
            foreach (var r in rows)
            {
                foreach (var c in r.Columns)
                {
                    raw.Add([r.Statement, r.IndexHandle, r.UserSeeks, r.UserScans, r.LastUserSeek,
                        r.AvgTotalUserCost, r.AvgUserImpact, c.ColumnId, c.ColumnUsage]);
                }
            }
            return new FakeDbDataReader(columns, raw);
        }
    }
}
