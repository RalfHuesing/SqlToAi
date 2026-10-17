using SqlToAi.Domain;

namespace SqlToAi.Security;

/// <summary>
/// Enforces database security policy guardrails.
/// </summary>
public interface ISecurityGuard
{
    bool IsTargetAllowed(DatabaseTargetContext target);
}
