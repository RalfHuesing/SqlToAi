using System.Data.Common;
using SqlToAi.Domain;

namespace SqlToAi.Database;

public interface IDatabaseConnectionFactory
{
    DbConnection CreateTargetConnection(DatabaseTargetContext target);

    DbConnection CreateTargetConnection(DatabaseTargetContext target, bool pooling) => CreateTargetConnection(target);

    // Catalog discovery alone may use the selected login's default database.
    Result<DbConnection> CreateCatalogConnection(string? serverAlias);
}
