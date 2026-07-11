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
using SqlToAi.Security;

namespace SqlToAi.Database;

/// <summary>
/// Executes a single read-only SQL SELECT statement safely inside an explicit rollback transaction.
/// Applies row limits and on-the-fly PII anonymization for <see cref="AccessLevel.ReadOnlyAnonymized"/> databases.
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

        // 4. Read-only guard: reject mutating statements
        if (!_readOnlyGuard.IsQuerySafe(query))
        {
            return SqlToAiError.WriteOperationBlocked("The query contains mutating SQL keywords and was rejected.");
        }

        // 5. Single-statement validation
        if (ContainsMultipleStatements(query))
        {
            return SqlToAiError.MultipleStatementsForbidden();
        }

        // 6. Resolve effective row limit (caller request capped by configured maximum)
        int effectiveLimit = requestedRowLimit.HasValue
            ? Math.Min(requestedRowLimit.Value, _options.MaxRowLimit)
            : _options.DefaultRowLimit;

        bool anonymize = accessLevel == AccessLevel.ReadOnlyAnonymized;

        try
        {
            using var connection = _connectionFactory.CreateConnection(databaseName);
            await connection.OpenAsync(cancellationToken);

            // Execute inside an explicit transaction that is always rolled back
            using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                var result = await ExecuteAndSerializeAsync(connection, transaction, query, effectiveLimit, anonymize, cancellationToken);
                return result;
            }
            finally
            {
                // Always roll back — guarantees zero side-effects even for accidental DML that slips through
                await transaction.RollbackAsync(cancellationToken);
            }
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

            sb.AppendLine(JsonSerializer.Serialize(rowDict, JsonSerializerOptions.Default));
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
    private static bool ContainsMultipleStatements(string query)
    {
        bool inSingleQuote = false;
        bool inBracket = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        ReadOnlySpan<char> span = query.AsSpan();

        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            char next = i + 1 < span.Length ? span[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++; // skip '/'
                }
                continue;
            }

            if (inSingleQuote)
            {
                if (c == '\'' && next == '\'') { i++; } // escaped quote
                else if (c == '\'') { inSingleQuote = false; }
                continue;
            }

            if (inBracket)
            {
                if (c == ']') inBracket = false;
                continue;
            }

            // Detect comment starts
            if (c == '-' && next == '-') { inLineComment = true; i++; continue; }
            if (c == '/' && next == '*') { inBlockComment = true; i++; continue; }

            // Detect literal/identifier starts
            if (c == '\'') { inSingleQuote = true; continue; }
            if (c == '[') { inBracket = true; continue; }

            // Semicolons outside literals/comments
            if (c == ';')
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
}
