#nullable enable

using System.Text;

namespace SqlToAi.Tests.AiNetLinter;

public sealed class AiNetLinterTests
{
    private const string LinterExePath = @"C:\Daten\AiNetLinter-win-x64\AiNetLinter.exe";

    [Fact]
    public async Task RunLinterShouldBeCleanOrBaselineMatch()
    {
        // 1. Skip test if the linter executable is not present on this machine
        if (!File.Exists(LinterExePath))
        {
            Assert.Skip("AiNetLinter.exe was not found at path: " + LinterExePath);
            return;
        }

        // 2. Find the solution root folder containing the .slnx file
        string solutionRoot = FindSolutionRoot();

        string configPath = Path.Combine(solutionRoot, "tests", "SqlToAi.Tests", "AiNetLinter", "rules", "SqlToAi.rules.json");
        string baselinePath = Path.Combine(solutionRoot, "tests", "SqlToAi.Tests", "AiNetLinter", "rules", "SqlToAi-baseline.json");
        string outputReportDir = Path.Combine(solutionRoot, "tests", "SqlToAi.Tests", "AiNetLinter", "output");
        string outputReportFile = Path.Combine(outputReportDir, "SqlToAi-linter-report.md");

        Directory.CreateDirectory(outputReportDir);

        // 3. Setup linter CLI arguments
        var args = new[]
        {
            "--config", $"\"{configPath}\"",
            "--path", $"\"{solutionRoot}\"",
            "--baseline", $"\"{baselinePath}\"",
            "--sync-cursor-rules"
        };

        string argumentsString = string.Join(" ", args);

        var startInfo = new ProcessStartInfo
        {
            FileName = LinterExePath,
            Arguments = argumentsString,
            WorkingDirectory = solutionRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // 4. Run linter process
        using var process = new Process { StartInfo = startInfo };
        
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        string stdout = stdoutBuilder.ToString();
        string stderr = stderrBuilder.ToString();
        int exitCode = process.ExitCode;

        // 5. Write report to output/SqlToAi-linter-report.md
        var reportContent = new StringBuilder();
        reportContent.AppendLine("# AiNetLinter Run Report");
        reportContent.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- **Timestamp:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        reportContent.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- **Exit Code:** {exitCode}");
        reportContent.AppendLine();
        reportContent.AppendLine("## Standard Output");
        reportContent.AppendLine("```");
        reportContent.AppendLine(stdout);
        reportContent.AppendLine("```");
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            reportContent.AppendLine();
            reportContent.AppendLine("## Standard Error");
            reportContent.AppendLine("```");
            reportContent.AppendLine(stderr);
            reportContent.AppendLine("```");
        }

        await File.WriteAllTextAsync(outputReportFile, reportContent.ToString(), Encoding.UTF8, TestContext.Current.CancellationToken);

        // 6. Assertions
        if (exitCode != 0)
        {
            Assert.Fail($"AiNetLinter failed with exit code {exitCode}. See report at: {outputReportFile}\r\nErrors:\r\n{stderr}\r\n{stdout}");
        }

        // 7. If exitCode == 0, move the synchronized cursor rules file to the target rules directory
        string cursorRulesFile = Path.Combine(solutionRoot, ".cursor", "rules", "AiNetLinter.mdc");
        Assert.True(File.Exists(cursorRulesFile), $"Linter was successful, but Cursor rules file was not synchronized/created at: {cursorRulesFile}");

        string targetRulesDir = Path.Combine(solutionRoot, ".agents", "rules");
        string targetRulesFile = Path.Combine(targetRulesDir, "AiNetLinter.mdc");

        Directory.CreateDirectory(targetRulesDir);
        if (File.Exists(targetRulesFile))
        {
            File.Delete(targetRulesFile);
        }
        File.Move(cursorRulesFile, targetRulesFile);

        // Clean up the .cursor/rules directory if it is now empty
        string cursorRulesDir = Path.Combine(solutionRoot, ".cursor", "rules");
        if (Directory.Exists(cursorRulesDir) && !Directory.EnumerateFileSystemEntries(cursorRulesDir).Any())
        {
            Directory.Delete(cursorRulesDir);
            
            // Also clean up .cursor directory if empty
            string cursorDir = Path.Combine(solutionRoot, ".cursor");
            if (Directory.Exists(cursorDir) && !Directory.EnumerateFileSystemEntries(cursorDir).Any())
            {
                Directory.Delete(cursorDir);
            }
        }

        Assert.True(File.Exists(targetRulesFile), $"Rules file was not found at target location: {targetRulesFile}");
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
