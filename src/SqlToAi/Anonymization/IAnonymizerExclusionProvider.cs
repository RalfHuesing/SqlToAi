#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SqlToAi.Anonymization;

/// <summary>
/// One table/column exclusion entry, optionally scoped to a specific schema. A null/empty
/// <see cref="SchemaName"/> means "any schema" — the historical, schema-agnostic behavior kept for
/// backward compatibility with configurations that never set a schema.
/// </summary>
/// <param name="SchemaName">The schema this entry is scoped to, or null/empty for "any schema".</param>
/// <param name="TableName">The table name this entry applies to.</param>
/// <param name="ColumnName">The column name this entry applies to.</param>
public sealed record AnonymizerExclusionEntry(string? SchemaName, string TableName, string ColumnName);

/// <summary>
/// An immutable, schema-aware set of table/column exclusions resolved once per database load (see
/// <see cref="IAnonymizerExclusionProvider.GetExclusionsAsync"/>). An entry with no schema
/// (<see cref="AnonymizerExclusionEntry.SchemaName"/> null/empty) matches any schema; an entry with
/// a schema set only matches when the resolved column's actual schema equals it (case-insensitive)
/// — so a same-named table in a different schema never inherits an exclusion meant for another
/// schema (see tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md, Finding
/// "Ausschluss-/Regel-Abgleich ist schema-blind — gleichnamige Tabelle in anderem Schema erbt
/// fremde Freigabe").
/// </summary>
public sealed class AnonymizerExclusionSet
{
    /// <summary>The empty set — used whenever no exclusion sources are configured or none loaded successfully.</summary>
    public static readonly AnonymizerExclusionSet Empty = new(Array.Empty<AnonymizerExclusionEntry>());

    private readonly IReadOnlyList<AnonymizerExclusionEntry> _entries;

    /// <summary>Initializes a new instance of the <see cref="AnonymizerExclusionSet"/> class.</summary>
    public AnonymizerExclusionSet(IReadOnlyList<AnonymizerExclusionEntry> entries)
    {
        _entries = entries;
    }

    /// <summary>The number of loaded exclusion entries — for tests/diagnostics.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Returns <c>true</c> when a loaded entry matches <paramref name="tableName"/>/<paramref name="columnName"/>
    /// exactly (case-insensitive) and either carries no schema restriction, or its schema equals
    /// <paramref name="schemaName"/> (case-insensitive). A null/empty <paramref name="schemaName"/>
    /// (the caller's resolved schema is unknown) only ever satisfies a schema-agnostic entry, never
    /// a schema-scoped one — fail-safe in the "keep anonymizing" direction.
    /// </summary>
    public bool Contains(string? schemaName, string tableName, string columnName)
    {
        foreach (var entry in _entries)
        {
            if (!string.Equals(entry.TableName, tableName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(entry.ColumnName, columnName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrEmpty(entry.SchemaName))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(schemaName) && string.Equals(entry.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Provides caching and retrieval of table/column exclusions for string anonymization on a per-database basis.
/// </summary>
public interface IAnonymizerExclusionProvider
{
    /// <summary>
    /// Retrieves the set of database-specific anonymizer exclusions, optionally schema-scoped (see
    /// <see cref="AnonymizerExclusionSet"/>).
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved exclusion set.</returns>
    Task<AnonymizerExclusionSet> GetExclusionsAsync(string databaseName, CancellationToken cancellationToken = default);
}
