#nullable enable

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Database;

/// <summary>
/// Service for measuring server-side query performance (STATISTICS IO/TIME) and parsing execution plans.
/// </summary>
public sealed class PerformanceMeasurementService : IPerformanceMeasurementService
{
    private static readonly Action<ILogger, string, Exception?> LogMeasurementFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, "MeasurementFailed"),
            "Performance measurement failed for database {Database}.");

    private static readonly Regex CpuTimeRegex = new(
        @"CPU time = (\d+) ms,\s+elapsed time = (\d+) ms", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IoReadsRegex = new(
        @"logical reads (\d+),\s+physical reads (\d+),\s+read-ahead reads (\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly ISecurityGuard _securityGuard;
    private readonly IAccessLevelProvider _accessLevelProvider;
    private readonly IReadOnlyGuard _readOnlyGuard;
    private readonly QueryExecutionOptions _options;
    private readonly ILogger<PerformanceMeasurementService> _logger;

    /// <summary>Initializes a new instance of <see cref="PerformanceMeasurementService"/>.</summary>
    public PerformanceMeasurementService(
        IDatabaseConnectionFactory connectionFactory,
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IReadOnlyGuard readOnlyGuard,
        IOptions<SqlToAiOptions> options,
        ILogger<PerformanceMeasurementService> logger)
    {
        _connectionFactory = connectionFactory;
        _securityGuard = securityGuard;
        _accessLevelProvider = accessLevelProvider;
        _readOnlyGuard = readOnlyGuard;
        _options = options.Value.QueryExecution;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<Result<PerformanceMeasurementResult>> MeasurePerformanceAsync(
        string databaseName,
        string query,
        CancellationToken cancellationToken = default)
    {
        return MeasurePerformanceAsync(new QueryPerformanceArgs(databaseName, query), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<PerformanceMeasurementResult>> MeasurePerformanceAsync(
        QueryPerformanceArgs args,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateArgs(args);
        if (validationError != null)
        {
            return validationError;
        }

        if (!_securityGuard.IsDatabaseAllowed(args.DatabaseName))
        {
            return SqlToAiError.SafetyCheckFailed(args.DatabaseName);
        }

        var accessLevel = await _accessLevelProvider.GetAccessLevelAsync(args.DatabaseName, cancellationToken);
        var guardError = ValidateSecurityGuards(args, accessLevel);
        if (guardError != null)
        {
            return guardError;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(args.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            try
            {
                var result = await ExecuteMeasurementAsync(connection, transaction, args, cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogMeasurementFailed(_logger, args.DatabaseName, ex);
            return SqlToAiErrorMapper.MapException(ex);
        }
    }

    private static SqlToAiError? ValidateArgs(QueryPerformanceArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.DatabaseName))
        {
            return SqlToAiError.InvalidParameters("Database name must not be empty.");
        }
        if (string.IsNullOrWhiteSpace(args.Query))
        {
            return SqlToAiError.InvalidParameters("Query must not be empty.");
        }
        return null;
    }

    private SqlToAiError? ValidateSecurityGuards(QueryPerformanceArgs args, AccessLevel accessLevel)
    {
        if (accessLevel == AccessLevel.None || accessLevel == AccessLevel.SchemaOnly)
        {
            return SqlToAiError.WriteOperationBlocked($"Database '{args.DatabaseName}' does not permit performance measurement (AccessLevel: {accessLevel}).");
        }

        bool writeAllowed = accessLevel == AccessLevel.ReadWrite;
        if (!writeAllowed && !_readOnlyGuard.IsQuerySafe(args.Query))
        {
            return SqlToAiError.WriteOperationBlocked("The query contains mutating SQL keywords and was rejected.");
        }

        if (SqlMultiStatementDetector.ContainsMultipleStatements(args.Query))
        {
            return SqlToAiError.MultipleStatementsForbidden();
        }

        return null;
    }

    private static async Task<PerformanceMeasurementResult> ExecuteMeasurementAsync(
        DbConnection connection,
        DbTransaction transaction,
        QueryPerformanceArgs args,
        CancellationToken ct)
    {
        var messages = new List<string>();
        if (connection is SqlConnection sqlConn)
        {
            sqlConn.InfoMessage += (_, e) => messages.Add(e.Message);
        }

        bool hasShowplanPermission = true;
        string? showplanNote = null;

        if (args.IncludePlanAnalysis)
        {
            try
            {
                await ExecuteSetOptionAsync(connection, transaction, "SET STATISTICS XML ON", ct);
            }
            catch (SqlException ex) when (ex.Number == 262 || ex.Message.Contains("SHOWPLAN", StringComparison.OrdinalIgnoreCase))
            {
                hasShowplanPermission = false;
                showplanNote = "SHOWPLAN permission missing; performance metrics captured without XML plan analysis.";
            }
        }

        await ExecuteSetOptionAsync(connection, transaction, "SET STATISTICS IO ON", ct);
        await ExecuteSetOptionAsync(connection, transaction, "SET STATISTICS TIME ON", ct);

        int warmupRuns = Math.Max(0, args.WarmupRuns);
        for (int i = 0; i < warmupRuns; i++)
        {
            await RunQueryOnceAsync(connection, transaction, args, ct);
        }

        messages.Clear();
        int execRuns = Math.Clamp(args.ExecutionRuns, 1, 10);
        string? xmlPlanText = null;

        for (int i = 0; i < execRuns; i++)
        {
            string? plan = await RunQueryOnceAsync(connection, transaction, args, ct);
            if (plan != null)
            {
                xmlPlanText = plan;
            }
        }

        var (metrics, warnings) = ProcessCapturedOutput(messages, xmlPlanText, execRuns, hasShowplanPermission);

        return new PerformanceMeasurementResult(
            Database: args.DatabaseName,
            RunsEvaluated: execRuns,
            WarmupRuns: warmupRuns,
            Metrics: metrics,
            Warnings: warnings,
            HasShowplanPermission: hasShowplanPermission,
            ShowplanNote: showplanNote);
    }

    private static async Task ExecuteSetOptionAsync(DbConnection connection, DbTransaction transaction, string sql, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string?> RunQueryOnceAsync(DbConnection connection, DbTransaction transaction, QueryPerformanceArgs args, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = args.Query;
        cmd.Transaction = transaction;
        SqlParameterBinder.BindParameters(cmd, args.Parameters);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        string? xmlPlanText = null;

        do
        {
            while (await reader.ReadAsync(ct))
            {
                if (reader.FieldCount == 1 && reader.GetName(0).StartsWith("Microsoft SQL Server 20", StringComparison.OrdinalIgnoreCase))
                {
                    xmlPlanText = reader.GetString(0);
                }
            }
        } while (await reader.NextResultAsync(ct));

        return xmlPlanText;
    }

    private static (PerformanceMetrics Metrics, IReadOnlyList<PerformancePlanWarning> Warnings) ProcessCapturedOutput(
        List<string> messages, string? xmlPlanText, int execRuns, bool hasShowplanPermission)
    {
        long totalCpu = 0, totalElapsed = 0, totalLogical = 0, totalPhysical = 0, totalReadAhead = 0;

        foreach (string msg in messages)
        {
            var cpuMatch = CpuTimeRegex.Match(msg);
            if (cpuMatch.Success)
            {
                totalCpu += long.Parse(cpuMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                totalElapsed += long.Parse(cpuMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            }

            var ioMatch = IoReadsRegex.Match(msg);
            if (ioMatch.Success)
            {
                totalLogical += long.Parse(ioMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                totalPhysical += long.Parse(ioMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                totalReadAhead += long.Parse(ioMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            }
        }

        var metrics = new PerformanceMetrics(
            CpuTimeMs: totalCpu / execRuns,
            ElapsedTimeMs: totalElapsed / execRuns,
            LogicalReads: totalLogical / execRuns,
            PhysicalReads: totalPhysical / execRuns,
            ReadAheadReads: totalReadAhead / execRuns);

        var warnings = hasShowplanPermission && !string.IsNullOrWhiteSpace(xmlPlanText)
            ? ParseExecutionPlanXml(xmlPlanText)
            : Array.Empty<PerformancePlanWarning>();

        return (metrics, warnings);
    }

    public static IReadOnlyList<PerformancePlanWarning> ParseExecutionPlanXml(string xmlText)
    {
        var warnings = new List<PerformancePlanWarning>();
        try
        {
            var doc = XDocument.Parse(xmlText);
            XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            ExtractMissingIndexWarnings(doc, ns, warnings);
            ExtractOperatorWarnings(doc, warnings);
        }
        catch (Exception ignored)
        {
            _ = ignored;
        }

        return warnings;
    }

    private static void ExtractMissingIndexWarnings(XDocument doc, XNamespace ns, List<PerformancePlanWarning> warnings)
    {
        foreach (var mi in doc.Descendants(ns + "MissingIndex"))
        {
            string table = mi.Attribute("Table")?.Value ?? "UnknownTable";
            string impactStr = mi.Parent?.Attribute("Impact")?.Value ?? mi.Parent?.Parent?.Attribute("Impact")?.Value ?? "0";
            double.TryParse(impactStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double impact);

            warnings.Add(new PerformancePlanWarning(
                Type: "MissingIndex",
                Severity: "Warning",
                Message: $"Missing index recommendation on {table} (Impact: {impact:F1}%).",
                Impact: impact));
        }
    }

    private static void ExtractOperatorWarnings(XDocument doc, List<PerformancePlanWarning> warnings)
    {
        foreach (var elem in doc.Descendants())
        {
            if (elem.Name.LocalName == "PlanAffectingConvert")
            {
                string expr = elem.Attribute("Expression")?.Value ?? "CONVERT_IMPLICIT";
                warnings.Add(new PerformancePlanWarning(
                    Type: "ImplicitConversion",
                    Severity: "Warning",
                    Message: $"Implicit conversion detected: {expr}.",
                    Impact: null));
            }
            else if (elem.Name.LocalName == "RelOp" && string.Equals(elem.Attribute("LogicalOp")?.Value, "Table Scan", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(new PerformancePlanWarning(
                    Type: "TableScan",
                    Severity: "Warning",
                    Message: "Table scan detected in execution plan.",
                    Impact: null));
            }
        }
    }
}
