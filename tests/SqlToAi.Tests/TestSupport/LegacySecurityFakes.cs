#nullable enable

using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Tests.TestSupport;

/// <summary>
/// Test double for <see cref="ISecurityGuard"/> returning a fixed <c>allowed</c> result for every
/// database name. Moved from <c>QueryExecutionServiceMockDb.cs</c> (step-003 / DRY-T1) so multiple
/// test classes can share one canonical implementation. Still consumed by
/// <c>IndexSuggestionServiceTests</c> directly, and by the <c>FakeQuerySafetyValidator</c>
/// delegation chain when a real <see cref="QuerySafetyValidator"/> is needed in service tests.
/// </summary>
internal sealed class FakeSecurityGuard(bool allowed) : ISecurityGuard
{
    public bool IsDatabaseAllowed(string databaseName) => allowed;
}

/// <summary>
/// Test double for <see cref="IAccessLevelProvider"/> returning a fixed <see cref="AccessLevel"/>
/// for every database name. Moved from <c>QueryExecutionServiceMockDb.cs</c> (step-003 / DRY-T1)
/// for the same reason as <see cref="FakeSecurityGuard"/>: shared by multiple test classes that
/// drive the guardrail pipeline through the security interfaces directly.
/// </summary>
internal sealed class FakeAccessLevelProvider(AccessLevel level) : IAccessLevelProvider
{
    public Task<AccessLevel> GetAccessLevelAsync(string databaseName, CancellationToken cancellationToken = default)
        => Task.FromResult(level);
}

/// <summary>
/// Test double for <see cref="IReadOnlyGuard"/> returning a fixed <c>safe</c> result for every
/// query. Moved from <c>QueryExecutionServiceMockDb.cs</c> (step-003 / DRY-T1). Note: pipeline
/// tests prefer the real <see cref="ReadOnlyGuard"/> so the regex-based detection
/// (<c>sp_executesql</c>, mutating keywords) is exercised end-to-end. This fake is only used
/// where a test wants to bypass the read-only check explicitly (e.g. <c>FakeQuerySafetyValidator</c>
/// in <c>QueryExecutionServiceMockDb.cs</c>).
/// </summary>
internal sealed class FakeReadOnlyGuard(bool safe) : IReadOnlyGuard
{
    public bool IsQuerySafe(string query) => safe;
}
