#nullable enable

namespace SqlToAi.Configuration;

/// <summary>
/// Root configuration for the file-based logging subsystem. All paths are resolved relative
/// to the directory containing the executable (<see cref="AppContext.BaseDirectory"/>), so the
/// logs live next to the .exe regardless of where the process was launched from.
/// </summary>
public sealed class LoggingOptions
{
    /// <summary>Root directory for all log files, relative to the executable directory.</summary>
    public string Directory { get; set; } = "log";

    /// <summary>Settings for the rolling application log (lifecycle events, request logs, info).</summary>
    public FileLogSinkOptions AppLog { get; set; } = new()
    {
        Level = "Information",
        RollingInterval = "Day",
        RetainedFileCount = 30
    };

    /// <summary>Settings for the rolling error log (Warning+ only, longer retention).</summary>
    public FileLogSinkOptions ErrorLog { get; set; } = new()
    {
        Level = "Warning",
        RollingInterval = "Day",
        RetainedFileCount = 90
    };

    /// <summary>Settings for the structured MCP tool-call trail (one JSONL per call).</summary>
    public McpTrailOptions McpTrail { get; set; } = new()
    {
        Enabled = true,
        Directory = "mcp",
        RetainedDays = 14
    };

    /// <summary>Resolves the absolute root log directory next to the running executable.</summary>
    public string GetAbsoluteRoot() =>
        Path.Combine(AppContext.BaseDirectory, Directory);
}

/// <summary>Options for a single rolling file sink.</summary>
public sealed class FileLogSinkOptions
{
    /// <summary>Whether this sink is enabled at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum log level. One of <c>Verbose</c>, <c>Debug</c>, <c>Information</c>, <c>Warning</c>,
    /// <c>Error</c>, <c>Fatal</c>. Matches Serilog's <c>LogEventLevel</c> names.
    /// </summary>
    public string Level { get; set; } = "Information";

    /// <summary>Rolling interval. One of <c>Day</c>, <c>Hour</c>, <c>Minute</c>, <c>Year</c>, <c>Month</c>, or <c>Infinite</c>.</summary>
    public string RollingInterval { get; set; } = "Day";

    /// <summary>How many rolled files to keep on disk before the oldest is deleted.</summary>
    public int RetainedFileCount { get; set; } = 30;
}

/// <summary>
/// Options for the structured MCP call trail. One JSONL file per call, grouped into
/// per-day subdirectories so the LLM activity is browsable in human time.
/// </summary>
public sealed class McpTrailOptions
{
    /// <summary>Whether the MCP trail is recorded at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Subdirectory (under <see cref="LoggingOptions.Directory"/>) where the trail lives.</summary>
    public string Directory { get; set; } = "mcp";

    /// <summary>How many days of MCP-trail files to keep. Files older than this are deleted on startup.</summary>
    public int RetainedDays { get; set; } = 14;
}
