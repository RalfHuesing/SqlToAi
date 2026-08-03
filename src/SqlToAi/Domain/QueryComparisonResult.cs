#nullable enable

using System.Text.Json.Serialization;

namespace SqlToAi.Domain;

/// <summary>
/// Represents the result of comparing two SQL queries for schema, row count, and content equivalence.
/// </summary>
/// <param name="IsEqual">True if both queries return identical schemas, row counts, and data rows.</param>
/// <param name="SchemaMatch">True if both queries produce matching column counts, names, and data types.</param>
/// <param name="CountMatch">True if both queries produce the exact same total row count.</param>
/// <param name="RowCountA">Total row count returned by Query A.</param>
/// <param name="RowCountB">Total row count returned by Query B.</param>
/// <param name="SchemaDifferences">List of schema mismatch descriptions if schemas differ.</param>
/// <param name="RowsInANotInB">JSON lines string of example rows present in A but missing in B (up to configured max diff limit).</param>
/// <param name="RowsInBNotInA">JSON lines string of example rows present in B but missing in A (up to configured max diff limit).</param>
public sealed record QueryComparisonResult(
    [property: JsonPropertyName("is_equal")] bool IsEqual,
    [property: JsonPropertyName("schema_match")] bool SchemaMatch,
    [property: JsonPropertyName("count_match")] bool CountMatch,
    [property: JsonPropertyName("row_count_a")] long RowCountA,
    [property: JsonPropertyName("row_count_b")] long RowCountB,
    [property: JsonPropertyName("schema_differences")] IReadOnlyList<string> SchemaDifferences,
    [property: JsonPropertyName("rows_in_a_not_in_b")] string RowsInANotInB,
    [property: JsonPropertyName("rows_in_b_not_in_a")] string RowsInBNotInA);
