#nullable enable

using Microsoft.Extensions.Options;
using SqlToAi.Configuration;

namespace SqlToAi.Database;

/// <summary>
/// Bundles the shared dependencies used by query execution-related database services.
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
    }

    internal IDatabaseConnectionFactory ConnectionFactory { get; }

    internal IQuerySafetyValidator QuerySafetyValidator { get; }

    internal QueryExecutionOptions QueryExecutionOptions { get; }
}
