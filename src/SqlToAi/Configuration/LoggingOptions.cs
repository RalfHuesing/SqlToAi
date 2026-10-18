namespace SqlToAi.Configuration;

/// <summary>
/// File logging paths are resolved against the shared per-user configuration root.
/// </summary>
public sealed class LoggingOptions
{
    /// <summary>Log root, absolute or relative to the user configuration directory.</summary>
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

    /// <summary>Resolves the log root independently of the working and installation directories.</summary>
    public string GetAbsoluteRoot() =>
        Path.IsPathFullyQualified(Directory) ? Directory : Path.GetFullPath(Directory, UserConfiguration.ResolveRoot());
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
