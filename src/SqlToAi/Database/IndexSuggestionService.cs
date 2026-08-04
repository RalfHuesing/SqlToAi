#nullable enable

using System.Data.Common;
using System.Globalization;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Database;

/// <summary>
/// Retrieves server-wide cumulative missing-index recommendations from
/// <c>sys.dm_db_missing_index_*</c> DMVs and renders them as a Markdown document.
/// Degrades gracefully (structured permission note instead of a hard error) when
/// the login lacks <c>VIEW SERVER STATE</c>.
/// </summary>
public sealed class IndexSuggestionService : IIndexSuggestionService
{
    private static readonly Action<ILogger, string, Exception?> LogSuggestFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, "SuggestFailed"),
            "Failed to load missing-index suggestions for database {Database}.");

    private static readonly string RestartHint =
        "Note: Missing-index statistics are cumulative since the last SQL Server restart. " +
        "On a freshly started server (or shortly after a restart) this list will be short or " +
        "empty; on long-running production servers it reflects the workload since startup.";

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly ISecurityGuard _securityGuard;
    private readonly IAccessLevelProvider _accessLevelProvider;
    private readonly IOptions<SqlToAiOptions> _options;
    private readonly ILogger<IndexSuggestionService> _logger;

    /// <summary>Initializes a new instance of <see cref="IndexSuggestionService"/>.</summary>
    public IndexSuggestionService(
        IDatabaseConnectionFactory connectionFactory,
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IOptions<SqlToAiOptions> options,
        ILogger<IndexSuggestionService> logger)
    {
        _connectionFactory = connectionFactory;
        _securityGuard = securityGuard;
        _accessLevelProvider = accessLevelProvider;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<Result<string>> SuggestIndexesAsync(
        string databaseName,
        string? tableName = null,
        double? minScore = null,
        int? top = null,
        CancellationToken cancellationToken = default)
    {
        int effectiveTop = top ?? 10;
        return SuggestIndexesAsync(
            new IndexSuggestionArgs(databaseName, tableName, minScore, effectiveTop),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<string>> SuggestIndexesAsync(
        IndexSuggestionArgs args,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(args.DatabaseName))
        {
            return SqlToAiError.InvalidParameters("Database name must not be empty.");
        }
        if (args.Top <= 0)
        {
            return SqlToAiError.InvalidParameters("top must be greater than zero.");
        }
        if (args.MinScore is < 0)
        {
            return SqlToAiError.InvalidParameters("min_score must be greater than or equal to zero.");
        }

        if (!_securityGuard.IsDatabaseAllowed(args.DatabaseName))
        {
            return SqlToAiError.SafetyCheckFailed($"Database '{args.DatabaseName}' is blocked by security policies (static whitelist).");
        }

        var accessLevel = await _accessLevelProvider.GetAccessLevelAsync(args.DatabaseName, cancellationToken);
        if (accessLevel == AccessLevel.None)
        {
            return SqlToAiError.SafetyCheckFailed($"Database '{args.DatabaseName}' access was denied (AccessLevel: None).");
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection(args.DatabaseName);
            await connection.OpenAsync(cancellationToken);

            IReadOnlyList<MissingIndexRow> rows = await LoadSuggestionsAsync(connection, args, cancellationToken);
            return RenderMarkdown(rows, args.DatabaseName);
        }
        catch (SqlException ex) when (IsViewServerStatePermissionError(ex))
        {
            return RenderPermissionNote(args.DatabaseName);
        }
        catch (Exception ex)
        {
            LogSuggestFailed(_logger, args.DatabaseName, ex);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    private static async Task<IReadOnlyList<MissingIndexRow>> LoadSuggestionsAsync(
        DbConnection connection,
        IndexSuggestionArgs args,
        CancellationToken ct)
    {
        const string sql = """
            WITH TopIndexes AS (
                SELECT TOP (@Top)
                    mid.statement AS Statement,
                    mig.index_handle AS IndexHandle,
                    migs.user_seeks AS UserSeeks,
                    migs.user_scans AS UserScans,
                    migs.last_user_seek AS LastUserSeek,
                    migs.avg_total_user_cost AS AvgTotalUserCost,
                    migs.avg_user_impact AS AvgUserImpact,
                    (migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans)) AS ImprovementScore
                FROM sys.dm_db_missing_index_group_stats AS migs
                INNER JOIN sys.dm_db_missing_index_groups AS mig
                    ON migs.index_group_handle = mig.index_group_handle
                INNER JOIN sys.dm_db_missing_index_details AS mid
                    ON mig.index_handle = mid.index_handle
                WHERE mid.database_id = DB_ID()
                  AND (@TableName IS NULL OR mid.statement LIKE '%' + @TableName + '%')
                  AND (@MinScore IS NULL OR ImprovementScore >= @MinScore)
                ORDER BY ImprovementScore DESC, mid.statement
            )
            SELECT
                ti.Statement,
                ti.IndexHandle,
                ti.UserSeeks,
                ti.UserScans,
                ti.LastUserSeek,
                ti.AvgTotalUserCost,
                ti.AvgUserImpact,
                mic.column_id AS ColumnId,
                mic.column_usage AS ColumnUsage
            FROM TopIndexes AS ti
            INNER JOIN sys.dm_db_missing_index_columns AS mic
                ON ti.IndexHandle = mic.index_handle
            ORDER BY ti.ImprovementScore DESC, ti.Statement, mic.column_id
            """;

        var parameters = new
        {
            TableName = args.TableName,
            MinScore = args.MinScore,
            Top = args.Top
        };

        var rawRows = await connection.QueryAsync<SuggestionRawRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return GroupRows(rawRows);
    }

    private static List<MissingIndexRow> GroupRows(IEnumerable<SuggestionRawRow> rawRows)
    {
        var byHandle = new Dictionary<long, MissingIndexRow>();

        foreach (var raw in rawRows)
        {
            if (!byHandle.TryGetValue(raw.IndexHandle, out var row))
            {
                double improvementScore = raw.AvgTotalUserCost * raw.AvgUserImpact * (raw.UserSeeks + raw.UserScans);
                row = new MissingIndexRow(
                    TableName: raw.Statement,
                    Seeks: raw.UserSeeks,
                    Scans: raw.UserScans,
                    LastSeek: raw.LastUserSeek,
                    ImprovementScore: improvementScore);
                byHandle[raw.IndexHandle] = row;
            }

            string columnId = raw.ColumnId.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(raw.ColumnUsage, "EQUALITY", StringComparison.OrdinalIgnoreCase))
            {
                row.AppendEqualityColumn(columnId);
            }
            else if (string.Equals(raw.ColumnUsage, "INEQUALITY", StringComparison.OrdinalIgnoreCase))
            {
                row.AppendInequalityColumn(columnId);
            }
            else if (string.Equals(raw.ColumnUsage, "INCLUDE", StringComparison.OrdinalIgnoreCase))
            {
                row.AppendIncludeColumn(columnId);
            }
        }

        return byHandle.Values.ToList();
    }

    private static string RenderMarkdown(IReadOnlyList<MissingIndexRow> rows, string databaseName)
    {
        var sb = new StringBuilder();
        sb.Append("# Missing Index Recommendations — ").Append(databaseName).AppendLine();
        sb.AppendLine();
        sb.AppendLine(RestartHint);
        sb.AppendLine();

        if (rows.Count == 0)
        {
            sb.Append("No missing-index recommendations found in database '").Append(databaseName).Append("'.");
            return sb.ToString();
        }

        var tableRows = new List<string[]>();
        foreach (var r in rows)
        {
            string score = Math.Round(r.ImprovementScore, 0).ToString(CultureInfo.InvariantCulture);
            string lastSeek = r.LastSeek?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
            string equality = string.Join(", ", r.EqualityColumns);
            string inequality = string.Join(", ", r.InequalityColumns);
            string include = string.Join(", ", r.IncludeColumns);
            tableRows.Add([score, r.TableName, equality, inequality, include,
                r.Seeks.ToString(CultureInfo.InvariantCulture),
                r.Scans.ToString(CultureInfo.InvariantCulture),
                lastSeek]);
        }

        sb.Append(MarkdownTableRenderer.Render(
            ["Score", "Table", "Equality Columns", "Inequality Columns", "Include Columns", "Seeks", "Scans", "Last Seek"],
            tableRows));
        return sb.ToString();
    }

    private static string RenderPermissionNote(string databaseName)
    {
        var sb = new StringBuilder();
        sb.Append("# Missing Index Recommendations — ").Append(databaseName).AppendLine();
        sb.AppendLine();
        sb.AppendLine(RestartHint);
        sb.AppendLine();
        sb.Append("**Note:** Missing-index statistics could not be loaded because the database user lacks the `VIEW SERVER STATE` permission. Grant the login `VIEW SERVER STATE` (server-scoped) to enable the `sql_suggest_indexes` tool, then retry. Until then, this tool will return this note instead of recommendation data.");
        return sb.ToString();
    }

    /// <summary>
    /// Detects a <c>VIEW SERVER STATE</c> permission failure using the general
    /// <see cref="PerformanceMeasurementService.IsPermissionError"/> helper.
    /// SQL Server raises error 300 (insufficient permission) and 297 (the user
    /// does not have permission to perform this action) when the DMV query is
    /// run without the server-scoped grant; the message text contains the
    /// <c>VIEW SERVER STATE</c> phrase as a reliable secondary signal.
    /// </summary>
    private static bool IsViewServerStatePermissionError(SqlException ex) =>
        PerformanceMeasurementService.IsPermissionError(ex, 300, "VIEW SERVER STATE")
        || PerformanceMeasurementService.IsPermissionError(ex, 297, "VIEW SERVER STATE");

    private sealed class MissingIndexRow
    {
        private readonly List<string> _equalityColumns = [];
        private readonly List<string> _inequalityColumns = [];
        private readonly List<string> _includeColumns = [];

        public MissingIndexRow(
            string TableName,
            long Seeks,
            long Scans,
            DateTime? LastSeek,
            double ImprovementScore)
        {
            this.TableName = TableName;
            this.Seeks = Seeks;
            this.Scans = Scans;
            this.LastSeek = LastSeek;
            this.ImprovementScore = ImprovementScore;
        }

        public string TableName { get; }
        public IReadOnlyList<string> EqualityColumns => _equalityColumns;
        public IReadOnlyList<string> InequalityColumns => _inequalityColumns;
        public IReadOnlyList<string> IncludeColumns => _includeColumns;
        public long Seeks { get; }
        public long Scans { get; }
        public DateTime? LastSeek { get; }
        public double ImprovementScore { get; }

        public void AppendEqualityColumn(string columnId) => _equalityColumns.Add(columnId);
        public void AppendInequalityColumn(string columnId) => _inequalityColumns.Add(columnId);
        public void AppendIncludeColumn(string columnId) => _includeColumns.Add(columnId);
    }

    private sealed class SuggestionRawRow
    {
        public string Statement { get; init; } = string.Empty;
        public long IndexHandle { get; init; }
        public long UserSeeks { get; init; }
        public long UserScans { get; init; }
        public DateTime? LastUserSeek { get; init; }
        public double AvgTotalUserCost { get; init; }
        public double AvgUserImpact { get; init; }
        public int ColumnId { get; init; }
        public string ColumnUsage { get; init; } = string.Empty;
    }
}
