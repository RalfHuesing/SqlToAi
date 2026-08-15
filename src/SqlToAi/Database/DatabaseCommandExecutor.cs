#nullable enable

using System.Data.Common;

namespace SqlToAi.Database;

/// <summary>
/// Shared database command execution helpers.
/// </summary>
internal static class DatabaseCommandExecutor
{
    /// <summary>
    /// Executes a single <c>SET ...</c> statement on the given connection and transaction.
    /// </summary>
    public static async Task ExecuteSetOptionAsync(DbConnection connection, DbTransaction transaction, string sql, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
