using System.Data.Common;
using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Factory for creating connections to the Microsoft SQL Server.
/// </summary>
public interface IDatabaseConnectionFactory
{
    DbConnection CreateTargetConnection(DatabaseTargetContext target);

    // Catalog discovery alone may use the selected login's default database.
    Result<DbConnection> CreateCatalogConnection(string? serverAlias);

    /// <summary>
    /// Creates and returns a new DbConnection to the specified database or the default database.
    /// </summary>
    /// <param name="databaseName">The target database name, or null for default database.</param>
    /// <returns>A DbConnection instance.</returns>
    DbConnection CreateConnection(string? databaseName = null);
}
