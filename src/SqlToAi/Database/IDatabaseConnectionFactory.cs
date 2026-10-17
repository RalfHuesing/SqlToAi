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
}
