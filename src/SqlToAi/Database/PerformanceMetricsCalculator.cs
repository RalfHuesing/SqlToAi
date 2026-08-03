#nullable enable

using System.Globalization;
using System.Text.RegularExpressions;
using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Computes aggregated <see cref="PerformanceMetrics"/> (min / avg / max) from per-run
/// STATISTICS IO/TIME message captures. Separated from <see cref="PerformanceMeasurementService"/>
/// so the pure calculation logic can be unit-tested without database infrastructure.
/// </summary>
internal static class PerformanceMetricsCalculator
{
    private static readonly Regex CpuTimeRegex = new(
        @"CPU time = (\d+) ms,\s+elapsed time = (\d+) ms", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IoReadsRegex = new(
        @"logical reads (\d+),\s+physical reads (\d+),\s+read-ahead reads (\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Computes <see cref="PerformanceMetrics"/> from per-run message captures.
    /// When <paramref name="execRuns"/> is 1, <c>Min*</c>/<c>Max*</c> fields are <c>null</c>.
    /// When <paramref name="execRuns"/> is &gt; 1, <c>Min*</c>/<c>Max*</c> are populated with the
    /// minimum and maximum values observed across all runs.
    /// </summary>
    public static PerformanceMetrics Compute(IReadOnlyList<IReadOnlyList<string>> perRunMessages, int execRuns)
    {
        long totalCpu = 0, totalElapsed = 0, totalLogical = 0, totalPhysical = 0, totalReadAhead = 0;
        long minCpu = long.MaxValue, maxCpu = long.MinValue;
        long minElapsed = long.MaxValue, maxElapsed = long.MinValue;

        foreach (var runMessages in perRunMessages)
        {
            var (runCpu, runElapsed, runLogical, runPhysical, runReadAhead) = ParseRunMessages(runMessages);
            totalCpu      += runCpu;
            totalElapsed  += runElapsed;
            totalLogical  += runLogical;
            totalPhysical += runPhysical;
            totalReadAhead += runReadAhead;

            if (runCpu > 0 || runElapsed > 0)
            {
                minCpu     = Math.Min(minCpu,     runCpu);
                maxCpu     = Math.Max(maxCpu,     runCpu);
                minElapsed = Math.Min(minElapsed, runElapsed);
                maxElapsed = Math.Max(maxElapsed, runElapsed);
            }
        }

        bool multiRun = execRuns > 1;
        int divisor   = execRuns > 0 ? execRuns : 1;

        return new PerformanceMetrics(
            CpuTimeMs:      totalCpu      / divisor,
            ElapsedTimeMs:  totalElapsed  / divisor,
            LogicalReads:   totalLogical  / divisor,
            PhysicalReads:  totalPhysical / divisor,
            ReadAheadReads: totalReadAhead / divisor,
            MinElapsedMs:   OrNullIfSingleRun(multiRun, minElapsed, long.MaxValue),
            MaxElapsedMs:   OrNullIfSingleRun(multiRun, maxElapsed, long.MinValue),
            MinCpuMs:       OrNullIfSingleRun(multiRun, minCpu,     long.MaxValue),
            MaxCpuMs:       OrNullIfSingleRun(multiRun, maxCpu,     long.MinValue));
    }

    /// <summary>
    /// Returns <paramref name="value"/> unless it's a single-run measurement or the value was never
    /// updated from its <paramref name="sentinel"/> (no run produced a non-zero CPU/elapsed time).
    /// </summary>
    private static long? OrNullIfSingleRun(bool multiRun, long value, long sentinel) =>
        multiRun && value != sentinel ? value : null;

    private static (long Cpu, long Elapsed, long Logical, long Physical, long ReadAhead) ParseRunMessages(
        IReadOnlyList<string> messages)
    {
        long cpu = 0, elapsed = 0, logical = 0, physical = 0, readAhead = 0;
        foreach (string msg in messages)
        {
            var cpuMatch = CpuTimeRegex.Match(msg);
            if (cpuMatch.Success)
            {
                cpu     += long.Parse(cpuMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                elapsed += long.Parse(cpuMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            }

            var ioMatch = IoReadsRegex.Match(msg);
            if (ioMatch.Success)
            {
                logical   += long.Parse(ioMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                physical  += long.Parse(ioMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                readAhead += long.Parse(ioMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            }
        }
        return (cpu, elapsed, logical, physical, readAhead);
    }
}
