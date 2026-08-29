#nullable enable

using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Database;

/// <summary>
/// Outcome of the guardrail pipeline: the resolved <see cref="AccessLevel"/> for the target
/// database plus the boolean indicating whether the current request is allowed to perform mutating
/// operations. The two pieces of information travel together because every caller that needs the
/// "is this query read-only safe?" decision also needs to know which access level that decision
/// was based on (e.g. to drive anonymization for <see cref="AccessLevel.ReadOnlyAnonymized"/>).
/// </summary>
public sealed record QuerySafetyCheckResult(AccessLevel AccessLevel, bool IsWriteAllowed);

/// <summary>
/// Single source of truth for the 6-stage guardrail pipeline that used to be duplicated across the
/// four query-processing services. Centralising the checks here removes the refactoring-drift
/// risk of the same logic living in <see cref="QueryExecutionService"/>,
/// <see cref="QueryValidationService"/>, <see cref="PerformanceMeasurementService"/>, and
/// <see cref="QueryComparisonService"/>.
/// </summary>
public interface IQuerySafetyValidator
{
    /// <summary>
    /// Runs the full 6-stage check (empty parameters, whitelist, access level, read-only guard,
    /// single-statement) and returns either the resolved safety outcome or the first error
    /// encountered.
    /// </summary>
    /// <param name="databaseName">Target database the query is meant to run against.</param>
    /// <param name="query">The SQL text to validate.</param>
    /// <param name="allowSchemaOnly">
    /// When <c>true</c>, <see cref="AccessLevel.SchemaOnly"/> is treated as a pass for stage 4
    /// (used by <see cref="QueryValidationService"/>, the only service whose business is to
    /// describe schema). All other services pass <c>false</c>, the project default.
    /// </param>
    /// <param name="cancellationToken">Forwarded to the dynamic access-level probe.</param>
    Task<Result<QuerySafetyCheckResult>> ValidateQuerySafetyAsync(
        string databaseName,
        string query,
        bool allowSchemaOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the guardrail pipeline for one script batch and permits the batch's intentional
    /// multiple statements while retaining all access-level and read-only checks.
    /// </summary>
    Task<Result<QuerySafetyCheckResult>> ValidateBatchSafetyAsync(
        string databaseName,
        string query,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc/>
internal sealed class QuerySafetyValidator : IQuerySafetyValidator
{
    private readonly ISecurityGuard _securityGuard;
    private readonly IAccessLevelProvider _accessLevelProvider;
    private readonly IReadOnlyGuard _readOnlyGuard;

    /// <summary>Initializes a new instance of <see cref="QuerySafetyValidator"/>.</summary>
    public QuerySafetyValidator(
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IReadOnlyGuard readOnlyGuard)
    {
        _securityGuard = securityGuard;
        _accessLevelProvider = accessLevelProvider;
        _readOnlyGuard = readOnlyGuard;
    }

    /// <inheritdoc/>
    public async Task<Result<QuerySafetyCheckResult>> ValidateQuerySafetyAsync(
        string databaseName,
        string query,
        bool allowSchemaOnly = false,
        CancellationToken cancellationToken = default)
    {
        return await ValidateAsync(
            databaseName, query, allowSchemaOnly, rejectMultipleStatements: true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result<QuerySafetyCheckResult>> ValidateBatchSafetyAsync(
        string databaseName,
        string query,
        CancellationToken cancellationToken = default)
    {
        return await ValidateAsync(
            databaseName, query, allowSchemaOnly: false, rejectMultipleStatements: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<QuerySafetyCheckResult>> ValidateAsync(
        string databaseName,
        string query,
        bool allowSchemaOnly,
        bool rejectMultipleStatements,
        CancellationToken cancellationToken)
    {
        // Stage 1: database name must be present.
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return SqlToAiError.InvalidParameters("Database name must not be empty.");
        }

        // Stage 2: query text must be present.
        if (string.IsNullOrWhiteSpace(query))
        {
            return SqlToAiError.InvalidParameters("Query must not be empty.");
        }

        // Stage 3: static whitelist check (configured allow/deny patterns).
        if (!_securityGuard.IsDatabaseAllowed(databaseName))
        {
            return SqlToAiError.SafetyCheckFailed(databaseName);
        }

        // Stage 4: dynamic access level probe (cached inside the provider).
        var accessLevel = await _accessLevelProvider
            .GetAccessLevelAsync(databaseName, cancellationToken)
            .ConfigureAwait(false);

        if (accessLevel == AccessLevel.None
            || (!allowSchemaOnly && accessLevel == AccessLevel.SchemaOnly))
        {
            return SqlToAiError.WriteOperationBlocked(
                $"Database '{databaseName}' is not permitted to run this query (AccessLevel: {accessLevel}).");
        }

        // Stage 5: read-only guard — rejected only when the database is not fully unlocked.
        // ReadWrite bypasses the keyword filter by design.
        bool writeAllowed = accessLevel == AccessLevel.ReadWrite;
        if (!writeAllowed && !_readOnlyGuard.IsQuerySafe(query))
        {
            return SqlToAiError.WriteOperationBlocked(
                "The query contains mutating SQL statements (e.g. INSERT, UPDATE, DELETE, MERGE, DDL, EXEC) and was rejected in read-only mode.");
        }

        // Stage 6: single-query calls enforce one statement; script batches intentionally skip
        // this boundary because the splitter already defines their execution unit.
        if (rejectMultipleStatements && SqlMultiStatementDetector.ContainsMultipleStatements(query))
        {
            return SqlToAiError.MultipleStatementsForbidden();
        }

        return new QuerySafetyCheckResult(accessLevel, writeAllowed);
    }
}
