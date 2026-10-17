using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Tests.TestSupport;

/// <summary>
/// Test double for <see cref="ISecurityGuard"/> returning a fixed <c>allowed</c> result for every
/// database target. Shared by multiple test classes that need a lightweight security double.
/// </summary>
internal sealed class FakeSecurityGuard(bool allowed) : ISecurityGuard
{
    public bool IsTargetAllowed(DatabaseTargetContext target) => allowed;
}

/// <summary>
/// Test double for <see cref="IAccessLevelProvider"/> returning a fixed <see cref="AccessLevel"/>
/// for every database target. Shared by multiple test classes that drive the guardrail pipeline
/// through the security interfaces directly.
/// </summary>
internal sealed class FakeAccessLevelProvider(AccessLevel level) : IAccessLevelProvider
{
    public Task<AccessLevel> GetTargetAccessLevelAsync(DatabaseTargetContext target, CancellationToken cancellationToken = default)
        => Task.FromResult(level);
}

/// <summary>
/// Test double for <see cref="IReadOnlyGuard"/> returning a fixed <c>safe</c> result for every
/// query. Note: pipeline tests prefer the real <see cref="ReadOnlyGuard"/> so the regex-based detection
/// (sp_executesql, mutating keywords) is exercised end-to-end. This fake is used where a test wants
/// to bypass the read-only check explicitly.
/// </summary>
internal sealed class FakeReadOnlyGuard(bool safe) : IReadOnlyGuard
{
    public bool IsQuerySafe(string query) => safe;
}
