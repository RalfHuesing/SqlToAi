using SqlToAi.Domain;

namespace SqlToAi.Database;

public interface IExplainQueryStatementGuard
{
    Result ValidateAccess(DatabaseTargetContext target, AccessLevel accessLevel);

    Result ValidateStatement(string query);

    Result Validate(DatabaseTargetContext target, AccessLevel accessLevel, string query);
}
