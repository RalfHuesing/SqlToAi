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
public sealed record QuerySafetyCheckResult(AccessLevel AccessLevel, bool IsWriteAllowed)
{
    // Preserve the caller's immutable target for downstream operations.
    public DatabaseTargetContext? Target { get; init; }
}

/// <summary>
/// Shared query and batch safety checks for an already resolved database target.
/// </summary>
public interface IQuerySafetyValidator
{
    Task<Result<QuerySafetyCheckResult>> ValidateTargetQuerySafetyAsync(
        DatabaseTargetContext target, string query, bool allowSchemaOnly = false,
        CancellationToken cancellationToken = default);

    Task<Result<QuerySafetyCheckResult>> ValidateTargetBatchSafetyAsync(
        DatabaseTargetContext target, string query, CancellationToken cancellationToken = default);
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

    public Task<Result<QuerySafetyCheckResult>> ValidateTargetQuerySafetyAsync(
        DatabaseTargetContext target, string query, bool allowSchemaOnly = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ValidateAsync(target, query, allowSchemaOnly,
            rejectMultipleStatements: true, cancellationToken);
    }

    public Task<Result<QuerySafetyCheckResult>> ValidateTargetBatchSafetyAsync(
        DatabaseTargetContext target, string query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ValidateAsync(target, query, allowSchemaOnly: false,
            rejectMultipleStatements: false, cancellationToken);
    }

    private async Task<Result<QuerySafetyCheckResult>> ValidateAsync(
        DatabaseTargetContext target,
        string query,
        bool allowSchemaOnly,
        bool rejectMultipleStatements,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return SqlToAiError.InvalidParameters("Query must not be empty.");
        }

        // Reject database context switching via USE statements across all access levels.
        if (SqlUseStatementDetector.ContainsUseStatement(query))
        {
            return SqlToAiError.SafetyCheckFailed("Switching database context via USE statement is not permitted; specify the target database in the tool arguments.");
        }

        if (!_securityGuard.IsTargetAllowed(target))
        {
            return SqlToAiError.SafetyCheckFailed(target.QualifiedName);
        }

        var accessLevel = await _accessLevelProvider.GetTargetAccessLevelAsync(target, cancellationToken).ConfigureAwait(false);

        if (accessLevel == AccessLevel.None
            || (!allowSchemaOnly && accessLevel == AccessLevel.SchemaOnly))
        {
            return SqlToAiError.WriteOperationBlocked(
                $"Database '{target.QualifiedName}' is not permitted to run this query (AccessLevel: {accessLevel}).");
        }

        // ReadWrite permits mutation; all other levels require a read-only statement.
        bool writeAllowed = accessLevel == AccessLevel.ReadWrite;
        if (!writeAllowed && !_readOnlyGuard.IsQuerySafe(query))
        {
            return SqlToAiError.WriteOperationBlocked(
                "The query contains mutating SQL statements (e.g. INSERT, UPDATE, DELETE, MERGE, DDL, EXEC) and was rejected in read-only mode.");
        }

        // Script batches allow multiple statements; single-query calls retain their boundary.
        if (rejectMultipleStatements && SqlMultiStatementDetector.ContainsMultipleStatements(query))
        {
            return SqlToAiError.MultipleStatementsForbidden();
        }

        return new QuerySafetyCheckResult(accessLevel, writeAllowed) { Target = target };
    }
}
