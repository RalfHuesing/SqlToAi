using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>
/// Pre-connection validation guard enforcing statement allowlist and database access policies for <c>sql_explain_query</c>.
/// </summary>
public interface IExplainQueryStatementGuard
{
    /// <summary>
    /// Validates database target permissions: requires SchemaOnly minimum; denies AccessLevel.None or excluded targets.
    /// </summary>
    Result ValidateAccess(DatabaseTargetContext target, AccessLevel accessLevel);

    /// <summary>
    /// Validates the SQL statement against the closed allowlist: optional leading DECLARE statements,
    /// followed by exactly one SELECT without INTO, with nothing following the SELECT.
    /// </summary>
    Result ValidateStatement(string query);

    /// <summary>
    /// Validates both database access and statement structure before any database connection.
    /// </summary>
    Result Validate(DatabaseTargetContext target, AccessLevel accessLevel, string query);
}
