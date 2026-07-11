#nullable enable

using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using SqlToAi.Configuration;
using SqlToAi.Mcp;

namespace SqlToAi.Tests.Mcp;

/// <summary>
/// Unit tests for <see cref="LogRetentionService"/>. Each test uses a fresh sandbox
/// directory so retention sweeps stay isolated.
/// </summary>
public sealed class LogRetentionServiceTests : IDisposable
{
    private readonly string _logRoot;

    public LogRetentionServiceTests()
    {
        _logRoot = Path.Combine(Path.GetTempPath(), "SqlToAiRetentionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_logRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_logRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Run_ShouldDeleteDaySubdirectoriesOlderThanRetention()
    {
        // Create a 30-day-old directory and a 1-day-old one.
        string oldDir = Path.Combine(_logRoot, "mcp", DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        string newDir = Path.Combine(_logRoot, "mcp", DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(newDir);
        File.WriteAllText(Path.Combine(oldDir, "old-call.jsonl"), "{}");
        File.WriteAllText(Path.Combine(newDir, "new-call.jsonl"), "{}");

        var service = CreateService(retainedDays: 14);
        service.Run();

        Assert.False(Directory.Exists(oldDir), "Old directory should be deleted.");
        Assert.True(Directory.Exists(newDir), "Recent directory should be kept.");
    }

    [Fact]
    public void Run_ShouldNotDelete_WhenRetentionIsZeroOrNegative()
    {
        string oldDir = Path.Combine(_logRoot, "mcp", DateTime.UtcNow.AddDays(-365).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(oldDir);

        var service = CreateService(retainedDays: 0);
        service.Run();

        Assert.True(Directory.Exists(oldDir), "RetainedDays=0 must mean 'keep everything'.");
    }

    [Fact]
    public void Run_ShouldSkipDirectories_ThatAreNotDateNames()
    {
        // Non-date subdirectories (e.g. a future "errors" sibling) must be left alone.
        string errorsDir = Path.Combine(_logRoot, "mcp", "errors");
        Directory.CreateDirectory(errorsDir);
        File.WriteAllText(Path.Combine(errorsDir, "junk.jsonl"), "{}");

        var service = CreateService(retainedDays: 1);
        service.Run();

        Assert.True(Directory.Exists(errorsDir));
    }

    [Fact]
    public void Run_ShouldDoNothing_WhenMcpTrailDisabled()
    {
        string oldDir = Path.Combine(_logRoot, "mcp", DateTime.UtcNow.AddDays(-100).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(oldDir);

        var options = new LoggingOptions
        {
            Directory = _logRoot,
            McpTrail = new McpTrailOptions { Enabled = false, RetainedDays = 1 }
        };
        var service = new LogRetentionService(options, NullLogger<LogRetentionService>.Instance);
        service.Run();

        Assert.True(Directory.Exists(oldDir));
    }

    private LogRetentionService CreateService(int retainedDays)
    {
        var options = new LoggingOptions
        {
            Directory = _logRoot,
            McpTrail = new McpTrailOptions { Enabled = true, Directory = "mcp", RetainedDays = retainedDays }
        };
        return new LogRetentionService(options, NullLogger<LogRetentionService>.Instance);
    }
}
