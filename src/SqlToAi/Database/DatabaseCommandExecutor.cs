using System.Data;
using System.Data.Common;

namespace SqlToAi.Database;

/// <summary>
/// Shared database command execution helpers.
/// </summary>
internal static class DatabaseCommandExecutor
{
    internal static async Task<IEnumerable<T>> QueryAsync<T>(
        DbConnection connection, string sql, Func<DatabaseRow, T> map,
        CancellationToken cancellationToken, IDictionary<string, object?>? parameters = null,
        int? commandTimeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var command = connection.CreateCommand();
        ConfigureQueryCommand(command, sql, parameters, commandTimeout);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        if (reader.FieldCount != 0)
        {
            var row = new DatabaseRow(reader);
            while (await reader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(map(row));
            }
        }
        await DrainResultsAsync(reader, cancellationToken);
        return rows;
    }

    internal static async Task<T> QueryFirstOrDefaultAsync<T>(
        DbConnection connection, string sql, Func<DatabaseRow, T> map,
        CancellationToken cancellationToken, IDictionary<string, object?>? parameters = null,
        int? commandTimeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var command = connection.CreateCommand();
        ConfigureQueryCommand(command, sql, parameters, commandTimeout);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        T result = default!;
        if (reader.FieldCount != 0 && await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = map(new DatabaseRow(reader));
            while (await reader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        await DrainResultsAsync(reader, cancellationToken);
        return result;
    }

    private static void ConfigureQueryCommand(DbCommand command, string sql,
        IDictionary<string, object?>? parameters, int? commandTimeout)
    {
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        if (commandTimeout.HasValue)
        {
            command.CommandTimeout = commandTimeout.Value;
        }
        SqlParameterBinder.BindParameters(command, parameters);
    }

    private static async Task DrainResultsAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Executes a single <c>SET ...</c> statement on the given connection and optional transaction.
    /// </summary>
    public static async Task ExecuteSetOptionAsync(DbConnection connection, DbTransaction? transaction, string sql, int commandTimeout, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = commandTimeout;
        cmd.Transaction = transaction;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
