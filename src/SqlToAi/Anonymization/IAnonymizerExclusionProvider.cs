#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SqlToAi.Anonymization;

/// <summary>
/// Provides caching and retrieval of table/column exclusions for string anonymization on a per-database basis.
/// </summary>
public interface IAnonymizerExclusionProvider
{
    /// <summary>
    /// Retrieves a set of database-specific anonymizer exclusions, formatted as "TableName.ColumnName" (lowercased).
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A HashSet containing the exclusions.</returns>
    Task<HashSet<string>> GetExclusionsAsync(string databaseName, CancellationToken cancellationToken = default);
}
