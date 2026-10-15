using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Metadata;
using SqlToAi.Security;

namespace SqlToAi.Tests.Integration;

/// <summary>
/// Shared fixture for live SQL integration tests.
/// Explicitly loads the copied DemoDB example profile instead of user settings.
/// </summary>
public sealed class SqlServerFixture
{
    public SqlToAiOptions Options { get; }
    public SqlConnectionFactory ConnectionFactory { get; }
    public SecurityGuard SecurityGuard { get; }
    public AccessLevelProvider AccessLevelProvider { get; }
    public ReadOnlyGuard ReadOnlyGuard { get; }
    public IQuerySafetyValidator QuerySafetyValidator { get; }
    public MetadataProvider MetadataProvider { get; }
    public SchemaService SchemaService { get; }
    public QueryExecutionService QueryExecutionService { get; }
    public QueryValidationService QueryValidationService { get; }
    public IndexSuggestionService IndexSuggestionService { get; }
    public Anonymizer Anonymizer { get; }
    public AnonymizationRuleProvider AnonymizationRuleProvider { get; }
    public AnonymizationPolicyResolver AnonymizationPolicyResolver { get; }
    public TokenVault TokenVault { get; }
    public QueryTokenResolver QueryTokenResolver { get; }

    public SqlServerFixture()
    {
        string appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.integration.json");
        if (!File.Exists(appsettingsPath))
            throw new FileNotFoundException("The explicit SQL integration demo profile was not copied to the test output.", appsettingsPath);
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(appsettingsPath)!)
            .AddJsonFile(Path.GetFileName(appsettingsPath), optional: false, reloadOnChange: false)
            .AddEnvironmentVariables("SQLTOAI_INTEGRATION_")
            .Build();

        Options = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>() ?? new SqlToAiOptions();
        ConfigurationResolver.Resolve(Options);

        var optionsWrapper = Microsoft.Extensions.Options.Options.Create(Options);
        ConnectionFactory   = new SqlConnectionFactory(optionsWrapper);
        SecurityGuard       = new SecurityGuard(optionsWrapper);
        AccessLevelProvider = new AccessLevelProvider(ConnectionFactory, optionsWrapper, NullLogger<AccessLevelProvider>.Instance);
        AnonymizationRuleProvider = new AnonymizationRuleProvider(ConnectionFactory, optionsWrapper, NullLogger<AnonymizationRuleProvider>.Instance);
        AnonymizationPolicyResolver = new AnonymizationPolicyResolver(optionsWrapper, AnonymizationRuleProvider);
        ReadOnlyGuard       = new ReadOnlyGuard();
        MetadataProvider    = new MetadataProvider(ConnectionFactory, optionsWrapper, NullLogger<MetadataProvider>.Instance);
        SchemaService       = new SchemaService(ConnectionFactory, SecurityGuard, AccessLevelProvider, MetadataProvider, AnonymizationPolicyResolver, optionsWrapper, NullLogger<SchemaService>.Instance);
        TokenVault          = new TokenVault();
        QueryTokenResolver  = new QueryTokenResolver(TokenVault, optionsWrapper);
        Anonymizer          = new Anonymizer(optionsWrapper, TokenVault);
        QuerySafetyValidator = new QuerySafetyValidator(SecurityGuard, AccessLevelProvider, ReadOnlyGuard);
        QueryExecutionService = new QueryExecutionService(ConnectionFactory, QuerySafetyValidator, new AnonymizationDependencies(Anonymizer, AnonymizationRuleProvider, QueryTokenResolver), optionsWrapper, NullLogger<QueryExecutionService>.Instance);
        QueryValidationService = new QueryValidationService(ConnectionFactory, QuerySafetyValidator, optionsWrapper, NullLogger<QueryValidationService>.Instance);
        IndexSuggestionService = new IndexSuggestionService(ConnectionFactory, SecurityGuard, AccessLevelProvider, optionsWrapper, NullLogger<IndexSuggestionService>.Instance);
    }

}

/// <summary>
/// xUnit v3 collection that shares a single <see cref="SqlServerFixture"/> across all integration test classes.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerCollectionFixture : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}

