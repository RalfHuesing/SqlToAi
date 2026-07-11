#nullable enable

using System.IO;
using Microsoft.Extensions.Logging;
using SqlToAi.Configuration;

namespace SqlToAi.Mcp;

/// <summary>
/// Deletes log files older than the configured retention horizon. Runs once at server
/// startup so the log directory does not grow unboundedly. The MCP trail uses one
/// subdirectory per day; older subdirectories are removed in full. The Serilog rolling
/// file sinks handle their own retention via <c>retainedFileCountLimit</c>, so the AppLog
/// and ErrorLog sections only need a sweep for files that pre-date the current rotation.
/// </summary>
public sealed class LogRetentionService
{
    private static readonly Action<ILogger, Exception?> LogSweepFailed =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1, "SweepFailed"),
            "MCP trail retention sweep failed.");

    private static readonly Action<ILogger, string, Exception?> LogDeleteFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2, "DeleteFailed"),
            "Could not delete expired MCP trail directory {Directory}.");

    private static readonly Action<ILogger, string, Exception?> LogDirectoryDeleted =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(3, "DirectoryDeleted"),
            "Deleted expired MCP trail directory {Directory}.");

    private readonly LoggingOptions _options;
    private readonly ILogger<LogRetentionService> _logger;

    /// <summary>Initializes a new instance of <see cref="LogRetentionService"/>.</summary>
    public LogRetentionService(LoggingOptions options, ILogger<LogRetentionService> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>Performs the retention sweep. Idempotent and safe to call on every startup.</summary>
    public void Run()
    {
        try
        {
            SweepMcpTrail();
        }
        catch (Exception ex)
        {
            // Retention is best-effort: never block startup on it.
            LogSweepFailed(_logger, ex);
        }
    }

    private void SweepMcpTrail()
    {
        if (!_options.McpTrail.Enabled) return;
        if (_options.McpTrail.RetainedDays <= 0) return;

        string trailRoot = Path.Combine(_options.GetAbsoluteRoot(), _options.McpTrail.Directory);
        if (!Directory.Exists(trailRoot)) return;

        DateTime cutoff = DateTime.UtcNow.AddDays(-_options.McpTrail.RetainedDays);

        foreach (string dayDir in Directory.EnumerateDirectories(trailRoot))
        {
            // Subdirectory name is the ISO date: yyyy-MM-dd. If the name is not a date, skip
            // rather than guess (e.g. a future "errors" subfolder would be left alone).
            string dirName = Path.GetFileName(dayDir);
            if (!DateTime.TryParse(dirName, out DateTime dirDate)) continue;
            if (dirDate >= cutoff) continue;

            try
            {
                Directory.Delete(dayDir, recursive: true);
                LogDirectoryDeleted(_logger, dayDir, null);
            }
            catch (Exception ex)
            {
                LogDeleteFailed(_logger, dayDir, ex);
            }
        }
    }
}
