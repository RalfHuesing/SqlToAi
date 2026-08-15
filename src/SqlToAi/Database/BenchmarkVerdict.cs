#nullable enable

namespace SqlToAi.Database;

/// <summary>
/// Canonical verdict values for the <c>sql_benchmark_optimization</c> MCP tool.
/// Defined as constants so the MCP output contract and the tool description
/// reference the same strings from a single source of truth.
/// </summary>
internal static class BenchmarkVerdict
{
    public const string Recommended = "Recommended";
    public const string NotRecommended = "NotRecommended";
    public const string Neutral = "Neutral";
    public const string UnsafeDueToDataMismatch = "UnsafeDueToDataMismatch";
}
