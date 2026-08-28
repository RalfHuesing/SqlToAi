#nullable enable

using System.Text;

namespace SqlToAi.Tests.AiNetLinter;

public sealed class AiNetLinterTests
{
    private static readonly string[] CandidatePaths =
    [
        @"C:\Daten\Tools\AiNetLinter-win-x64\AiNetLinter.exe",
        @"C:\Daten\Tools\Beta\AiNetLinter-win-x64\AiNetLinter.exe",
        @"C:\Daten\AiNetLinter-win-x64\AiNetLinter.exe"
    ];

    private static string? ResolveLinterExePath()
    {
        return CandidatePaths.FirstOrDefault(File.Exists);
    }

    [Fact]
    public async Task RunLinterShouldBeClean()
    {
        string? linterExePath = ResolveLinterExePath();
        if (linterExePath == null)
        {
            Assert.Skip("AiNetLinter.exe was not found in candidate paths.");
            return;
        }

        // 2. Find the solution root folder containing the .slnx file
        string solutionRoot = FindSolutionRoot();

        string configPath = Path.Combine(solutionRoot, "tests", "SqlToAi.Tests", "AiNetLinter", "rules", "SqlToAi.rules.json");
        string outputReportDir = Path.Combine(solutionRoot, "tests", "SqlToAi.Tests", "AiNetLinter", "output");
        string outputReportFile = Path.Combine(outputReportDir, "SqlToAi-linter-report.md");
        string targetRulesFile = Path.Combine(solutionRoot, ".agents", "rules", "AiNetLinter.mdc");

        Directory.CreateDirectory(outputReportDir);

        // 3. Run code quality validation (no baseline — full clean check)
        var validationArgs = new[]
        {
            "--config", $"\"{configPath}\"",
            "--path", $"\"{solutionRoot}\""
        };

        var (valExitCode, valStdout, valStderr) = await RunLinterProcessAsync(
            linterExePath, string.Join(" ", validationArgs), solutionRoot, TestContext.Current.CancellationToken);

        // 4. Always write report — even on failure, so the agent can read it
        var reportContent = new StringBuilder();
        reportContent.AppendLine("# AiNetLinter Run Report");
        reportContent.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- **Timestamp:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        reportContent.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- **Validation Exit Code:** {valExitCode}");
        reportContent.AppendLine();
        reportContent.AppendLine("## Validation Output");
        reportContent.AppendLine("```");
        reportContent.AppendLine(valStdout);
        reportContent.AppendLine("```");
        if (!string.IsNullOrWhiteSpace(valStderr))
        {
            reportContent.AppendLine();
            reportContent.AppendLine("## Validation Error");
            reportContent.AppendLine("```");
            reportContent.AppendLine(valStderr);
            reportContent.AppendLine("```");
        }

        await File.WriteAllTextAsync(outputReportFile, reportContent.ToString(), Encoding.UTF8, TestContext.Current.CancellationToken);

        // 5. Assertions on validation success
        if (valExitCode != 0)
        {
            Assert.Fail($"AiNetLinter validation failed with exit code {valExitCode}. See report at: {outputReportFile}\r\nErrors:\r\n{valStderr}\r\n{valStdout}");
        }

        // 6. Step 2: Run rules synchronization (only if validation succeeded)
        Directory.CreateDirectory(Path.GetDirectoryName(targetRulesFile)!);

        var syncArgs = new[]
        {
            "--config", $"\"{configPath}\"",
            "--path", $"\"{solutionRoot}\"",
            "--sync-agent-rules",
            "--agent-rules-path", $"\"{targetRulesFile}\""
        };

        var (syncExitCode, syncStdout, syncStderr) = await RunLinterProcessAsync(
            linterExePath, string.Join(" ", syncArgs), solutionRoot, TestContext.Current.CancellationToken);

        if (syncExitCode != 0)
        {
            Assert.Fail($"AiNetLinter rules synchronization failed with exit code {syncExitCode}.\r\nErrors:\r\n{syncStderr}\r\n{syncStdout}");
        }

        Assert.True(File.Exists(targetRulesFile), $"Rules file was not found at target location: {targetRulesFile}");
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunLinterProcessAsync(
        string exePath, string argumentsString, string solutionRoot, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = argumentsString,
            WorkingDirectory = solutionRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }

    private static string FindSolutionRoot()
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null)
        {
            if (currentDir.GetFiles("SqlToAi.slnx").Length > 0)
            {
                return currentDir.FullName;
            }
            currentDir = currentDir.Parent;
        }
        throw new DirectoryNotFoundException("Solution root folder with SqlToAi.slnx not found.");
    }
}
