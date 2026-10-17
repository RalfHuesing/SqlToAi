using Microsoft.Extensions.Options;
using SqlToAi.Configuration;

namespace SqlToAi.Database;

/// <summary>
/// Bundles the shared dependencies used by query execution-related database services. The target
/// resolver lets those services resolve caller arguments once at their entry points and carry the
/// immutable context downstream.
/// </summary>
internal sealed class QueryExecutionDependencies
{
    internal QueryExecutionDependencies(
        IDatabaseConnectionFactory connectionFactory,
        IQuerySafetyValidator querySafetyValidator,
        IOptions<SqlToAiOptions> options)
    {
        ConnectionFactory = connectionFactory;
        QuerySafetyValidator = querySafetyValidator;
        QueryExecutionOptions = options.Value.QueryExecution;
        TargetResolver = new DatabaseTargetResolver(options.Value);
    }

    internal IDatabaseConnectionFactory ConnectionFactory { get; }

    internal IQuerySafetyValidator QuerySafetyValidator { get; }

    internal QueryExecutionOptions QueryExecutionOptions { get; }

    internal DatabaseTargetResolver TargetResolver { get; }
}
