#nullable enable

using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Security;

namespace SqlToAi.Database;

/// <summary>
/// Executes a single SQL statement. For every database except those at
/// <see cref="AccessLevel.ReadWrite"/>, the statement runs inside an explicit rollback
/// transaction and mutating keywords are rejected outright. Applies row limits and on-the-fly
/// PII anonymization for <see cref="AccessLevel.ReadOnlyAnonymized"/> databases.
/// </summary>
public sealed class QueryExecutionService : IQueryExecutionService
{
    private static readonly Action<ILogger, string, Exception?> LogQueryFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, "QueryFailed"),
            "Query execution failed for database {Database}.");

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly ISecurityGuard _securityGuard;
    private readonly IAccessLevelProvider _accessLevelProvider;
    private readonly IReadOnlyGuard _readOnlyGuard;
    private readonly IAnonymizer _anonymizer;
    private readonly QueryExecutionOptions _options;
    private readonly ILogger<QueryExecutionService> _logger;

    /// <summary>Initializes a new instance of <see cref="QueryExecutionService"/>.</summary>
    public QueryExecutionService(
        IDatabaseConnectionFactory connectionFactory,
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IReadOnlyGuard readOnlyGuard,
        IAnonymizer anonymizer,
        IOptions<SqlToAiOptions> options,
        ILogger<QueryExecutionService> logger)
    {
        _connectionFactory = connectionFactory;
        _securityGuard = securityGuard;
        _accessLevelProvider = accessLevelProvider;
        _readOnlyGuard = readOnlyGuard;
        _anonymizer = anonymizer;
        _options = options.Value.QueryExecution;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<string>> ExecuteQueryAsync(
        string databaseName,
        string query,
        int? requestedRowLimit,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate inputs
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return SqlToAiError.InvalidParameters("Database name must not be empty.");
        }
        if (string.IsNullOrWhiteSpace(query))
        {
            return SqlToAiError.InvalidParameters("Query must not be empty.");
        }

        // 2. Static whitelist check
        if (!_securityGuard.IsDatabaseAllowed(databaseName))
        {
            return SqlToAiError.SafetyCheckFailed(databaseName);
        }

        // 3. Dynamic access level check
        var accessLevel = await _accessLevelProvider.GetAccessLevelAsync(databaseName, cancellationToken);

        if (accessLevel == AccessLevel.None || accessLevel == AccessLevel.SchemaOnly)
        {
            return SqlToAiError.WriteOperationBlocked($"Database '{databaseName}' does not permit query execution (AccessLevel: {accessLevel}).");
        }

        // 4. Read-only guard: reject mutating statements, unless this database is fully
        //    unlocked via AccessCheckSql returning ReadWrite.
        bool writeAllowed = accessLevel == AccessLevel.ReadWrite;

        if (!writeAllowed && !_readOnlyGuard.IsQuerySafe(query))
        {
            return SqlToAiError.WriteOperationBlocked("The query contains mutating SQL keywords and was rejected.");
        }

        // 5. Single-statement validation — always enforced, write-allowed or not, to keep
        //    the blast radius of a single call limited to one statement.
        if (ContainsMultipleStatements(query))
        {
            return SqlToAiError.MultipleStatementsForbidden();
        }

        // 6. Resolve effective row limit (caller request capped by configured maximum)
        int effectiveLimit = requestedRowLimit.HasValue
            ? Math.Min(requestedRowLimit.Value, _options.MaxRowLimit)
            : _options.DefaultRowLimit;

        bool anonymize = accessLevel == AccessLevel.ReadOnlyAnonymized;

        return await ExecuteQueryInTransactionAsync(
            databaseName, query, effectiveLimit, anonymize, writeAllowed, cancellationToken);
    }

    private async Task<Result<string>> ExecuteQueryInTransactionAsync(
        string databaseName,
        string query,
        int effectiveLimit,
        bool anonymize,
        bool writeAllowed,
        CancellationToken cancellationToken)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            Result<string> result;
            try
            {
                result = await ExecuteAndSerializeAsync(connection, transaction, query, effectiveLimit, anonymize, cancellationToken);
            }
            catch
            {
                // Roll back any partial state before the outer catch reports the error.
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            // Only a write-allowed database persists changes; everything else stays a dry run
            // (guarantees zero side-effects even for accidental DML that slips through).
            if (writeAllowed)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogQueryFailed(_logger, databaseName, ex);
            return SqlToAiError.QueryError(ex.Message);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<Result<string>> ExecuteAndSerializeAsync(
        DbConnection connection,
        DbTransaction transaction,
        string query,
        int rowLimit,
        bool anonymize,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.Transaction = transaction;
        command.CommandTimeout = 0; // governed by caller's CancellationToken

        using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);

        var columnNames = GetColumnNames(reader);
        var sb = new StringBuilder();
        int rowCount = 0;

        while (rowCount < rowLimit && await reader.ReadAsync(cancellationToken))
        {
            var rowDict = new Dictionary<string, object?>(columnNames.Length, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < columnNames.Length; i++)
            {
                object? raw = reader.IsDBNull(i) ? null : reader.GetValue(i);

                if (anonymize && raw is string strVal)
                {
                    raw = _anonymizer.Anonymize(columnNames[i], strVal);
                }

                rowDict[columnNames[i]] = raw;
            }

            sb.AppendLine(JsonSerializer.Serialize(rowDict, typeof(Dictionary<string, object?>), McpJsonContext.Default));
            rowCount++;
        }

        if (rowCount == 0)
        {
            return "[]";
        }

        return sb.ToString().TrimEnd();
    }

    private static string[] GetColumnNames(DbDataReader reader)
    {
        var names = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
        {
            names[i] = reader.GetName(i);
        }
        return names;
    }

    /// <summary>
    /// Detects multiple SQL statements by scanning for semicolons outside
    /// string literals (<c>'...'</c>), bracket identifiers (<c>[...]</c>), and comments (<c>--</c>, <c>/* */</c>).
    /// </summary>
    private enum SqlParserState
    {
        Normal,
        LineComment,
        BlockComment,
        SingleQuote,
        Bracket
    }

    private static bool ContainsMultipleStatements(string query)
    {
        var state = SqlParserState.Normal;
        ReadOnlySpan<char> span = query.AsSpan();

        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            char next = i + 1 < span.Length ? span[i + 1] : '\0';

            state = Transition(state, c, next, ref i);

            if (state == SqlParserState.Normal && c == ';')
            {
                // Allow trailing semicolon at end (after trimming whitespace)
                string remaining = query[(i + 1)..].TrimEnd();
                if (remaining.Length > 0)
                {
                    return true; // text after semicolon → second statement
                }
            }
        }

        return false;
    }

    private static SqlParserState Transition(SqlParserState state, char c, char next, ref int i)
    {
        switch (state)
        {
            case SqlParserState.LineComment:
                if (c == '\n') return SqlParserState.Normal;
                return SqlParserState.LineComment;

            case SqlParserState.BlockComment:
                if (c == '*' && next == '/')
                {
                    i++; // skip '/'
                    return SqlParserState.Normal;
                }
                return SqlParserState.BlockComment;

            case SqlParserState.SingleQuote:
                if (c == '\'' && next == '\'')
                {
                    i++; // escaped quote
                    return SqlParserState.SingleQuote;
                }
                if (c == '\'') return SqlParserState.Normal;
                return SqlParserState.SingleQuote;

            case SqlParserState.Bracket:
                if (c == ']') return SqlParserState.Normal;
                return SqlParserState.Bracket;

            default:
                if (c == '-' && next == '-')
                {
                    i++;
                    return SqlParserState.LineComment;
                }
                if (c == '/' && next == '*')
                {
                    i++;
                    return SqlParserState.BlockComment;
                }
                if (c == '\'') return SqlParserState.SingleQuote;
                if (c == '[') return SqlParserState.Bracket;
                return SqlParserState.Normal;
        }
    }
}
