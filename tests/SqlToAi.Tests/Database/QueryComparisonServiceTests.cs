#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Service-level placeholders for <see cref="QueryComparisonService"/>. The pure pipeline
/// outcomes (empty parameters, blocked database, access level, mutating-keyword detection,
/// multi-statement detection) are covered end-to-end in the dedicated
/// <c>QuerySafetyValidatorTests</c> class (step-003 / DRY-T3). The service runs the
/// <see cref="IQuerySafetyValidator"/> pipeline twice (once for QueryA, once for QueryB) and
/// short-circuits on the first failure. End-to-end coverage of the 2-query comparison flow
/// belongs in the integration tests; unit-level pin of the pipeline is the validator's job.
/// </summary>
public sealed class QueryComparisonServiceTests
{
    private static QueryComparisonService BuildService(
        bool isAllowed = true,
        AccessLevel accessLevel = AccessLevel.ReadOnly,
        SqlToAiError? error = null)
    {
        var options = new SqlToAiOptions();

        IQuerySafetyValidator safetyValidator = error != null
            ? new FakeQuerySafetyValidator(error)
            : new FakeQuerySafetyValidator(
                new FakeSecurityGuard(isAllowed),
                new FakeAccessLevelProvider(accessLevel),
                new ReadOnlyGuard());

        return new QueryComparisonService(
            new ValidationMockConnectionFactory(),
            safetyValidator,
            Options.Create(options),
            NullLogger<QueryComparisonService>.Instance);
    }
}
