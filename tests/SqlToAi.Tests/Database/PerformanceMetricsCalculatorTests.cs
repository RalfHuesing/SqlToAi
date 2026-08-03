#nullable enable

using SqlToAi.Database;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for <see cref="PerformanceMetricsCalculator"/>, verifying min/avg/max computation
/// across single and multiple execution runs.
/// </summary>
// @covers PerformanceMetricsCalculator
public sealed class PerformanceMetricsCalculatorTests
{
    private static IReadOnlyList<string> MakeRunMessages(
        long cpuMs, long elapsedMs,
        long logicalReads = 10, long physicalReads = 0, long readAheadReads = 0)
    {
        return
        [
            $"SQL Server Execution Times:\r\n   CPU time = {cpuMs} ms,  elapsed time = {elapsedMs} ms.",
            $"Table 'Orders'. Scan count 1, logical reads {logicalReads}, " +
            $"physical reads {physicalReads}, read-ahead reads {readAheadReads}."
        ];
    }

    [Fact]
    public void Compute_SingleRun_ReturnsNullMinMax()
    {
        // Single run: min/max fields must be null (no comparison possible)
        var perRun = new List<IReadOnlyList<string>> { MakeRunMessages(cpuMs: 100, elapsedMs: 200) };

        var result = PerformanceMetricsCalculator.Compute(perRun, execRuns: 1);

        Assert.Equal(100L, result.CpuTimeMs);
        Assert.Equal(200L, result.ElapsedTimeMs);
        Assert.Null(result.MinCpuMs);
        Assert.Null(result.MaxCpuMs);
        Assert.Null(result.MinElapsedMs);
        Assert.Null(result.MaxElapsedMs);
    }

    [Fact]
    public void Compute_ThreeRuns_ReturnsCorrectAvgMinMax()
    {
        // Three runs with different values
        var perRun = new List<IReadOnlyList<string>>
        {
            MakeRunMessages(cpuMs: 100, elapsedMs: 110),
            MakeRunMessages(cpuMs: 200, elapsedMs: 220),
            MakeRunMessages(cpuMs: 150, elapsedMs: 165)
        };

        var result = PerformanceMetricsCalculator.Compute(perRun, execRuns: 3);

        // avg: (100+200+150)/3 = 150
        Assert.Equal(150L, result.CpuTimeMs);
        // avg elapsed: (110+220+165)/3 = 165
        Assert.Equal(165L, result.ElapsedTimeMs);
        // min/max cpu
        Assert.Equal(100L, result.MinCpuMs);
        Assert.Equal(200L, result.MaxCpuMs);
        // min/max elapsed
        Assert.Equal(110L, result.MinElapsedMs);
        Assert.Equal(220L, result.MaxElapsedMs);
    }

    [Fact]
    public void Compute_TwoRuns_AllFieldsPresent()
    {
        // Two runs: multiRun=true, min/max must be populated
        var perRun = new List<IReadOnlyList<string>>
        {
            MakeRunMessages(cpuMs: 50, elapsedMs: 60, logicalReads: 5),
            MakeRunMessages(cpuMs: 70, elapsedMs: 80, logicalReads: 15)
        };

        var result = PerformanceMetricsCalculator.Compute(perRun, execRuns: 2);

        Assert.Equal(60L,  result.CpuTimeMs);       // avg (50+70)/2
        Assert.Equal(70L,  result.ElapsedTimeMs);    // avg (60+80)/2
        Assert.Equal(10L,  result.LogicalReads);     // avg (5+15)/2
        Assert.Equal(50L,  result.MinCpuMs);
        Assert.Equal(70L,  result.MaxCpuMs);
        Assert.Equal(60L,  result.MinElapsedMs);
        Assert.Equal(80L,  result.MaxElapsedMs);
    }

    [Fact]
    public void Compute_EmptyRunMessages_ReturnsZeroMetricsNullMinMax()
    {
        // Empty message lists produce zeros; min/max are null for single run
        var perRun = new List<IReadOnlyList<string>> { Array.Empty<string>() };

        var result = PerformanceMetricsCalculator.Compute(perRun, execRuns: 1);

        Assert.Equal(0L, result.CpuTimeMs);
        Assert.Equal(0L, result.ElapsedTimeMs);
        Assert.Null(result.MinCpuMs);
        Assert.Null(result.MaxCpuMs);
    }

    [Fact]
    public void Compute_MixedZeroAndNonZeroRuns_MinIsZeroNotNull()
    {
        // A run with a genuine 0 ms measurement must count toward min/max, not be excluded
        var perRun = new List<IReadOnlyList<string>>
        {
            MakeRunMessages(cpuMs: 0, elapsedMs: 0),
            MakeRunMessages(cpuMs: 50, elapsedMs: 60)
        };

        var result = PerformanceMetricsCalculator.Compute(perRun, execRuns: 2);

        Assert.Equal(0L, result.MinCpuMs);
        Assert.Equal(0L, result.MinElapsedMs);
        Assert.Equal(50L, result.MaxCpuMs);
        Assert.Equal(60L, result.MaxElapsedMs);
    }

    [Fact]
    public void Compute_AllRunsZero_MinMaxAreZeroNotNull()
    {
        // Every run genuinely measures 0 ms: min/max must be 0, not null
        var perRun = new List<IReadOnlyList<string>>
        {
            MakeRunMessages(cpuMs: 0, elapsedMs: 0),
            MakeRunMessages(cpuMs: 0, elapsedMs: 0)
        };

        var result = PerformanceMetricsCalculator.Compute(perRun, execRuns: 2);

        Assert.Equal(0L, result.MinCpuMs);
        Assert.Equal(0L, result.MaxCpuMs);
        Assert.Equal(0L, result.MinElapsedMs);
        Assert.Equal(0L, result.MaxElapsedMs);
    }
}
