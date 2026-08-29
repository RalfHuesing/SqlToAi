#nullable enable

using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;

namespace SqlToAi.Tests.Database;

public sealed class QueryExecutionDependenciesTests
{
    [Fact]
    public void Constructor_BundlesDependenciesAndCapturesQueryExecutionOptions()
    {
        var connectionFactory = new ValidationMockConnectionFactory();
        var querySafetyValidator = FakeQuerySafetyValidator.Create();
        var queryExecutionOptions = new QueryExecutionOptions { MaxRowLimit = 42 };
        var options = Options.Create(new SqlToAiOptions { QueryExecution = queryExecutionOptions });

        var dependencies = new QueryExecutionDependencies(connectionFactory, querySafetyValidator, options);

        Assert.Same(connectionFactory, dependencies.ConnectionFactory);
        Assert.Same(querySafetyValidator, dependencies.QuerySafetyValidator);
        Assert.Same(queryExecutionOptions, dependencies.QueryExecutionOptions);
    }
}
