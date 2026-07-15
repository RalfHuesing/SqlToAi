#nullable enable

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
/// Shared fixture for integration tests that exercise the real services against a live SQL Server.
/// Reads configuration from <c>src/SqlToAi/appsettings.json</c> (the same file the runtime uses)
/// and builds the real DI graph — no mocks, no test doubles.
///
/// Tests are tagged with <c>[Trait("Category", "Integration")]</c>. To run them in isolation:
///   <c>dotnet test --filter "Category=Integration"</c>
/// To run everything except integration tests:
///   <c>dotnet test --filter "Category!=Integration"</c>
///
/// If the database is unreachable, every test in this collection is reported as failed with the
/// underlying connection error — that is intentional, surfacing the misconfiguration loudly rather
/// than silently skipping.
/// </summary>
public sealed class SqlServerFixture
{
    public SqlToAiOptions Options { get; }
    public SqlConnectionFactory ConnectionFactory { get; }
    public SecurityGuard SecurityGuard { get; }
    public AccessLevelProvider AccessLevelProvider { get; }
    public ReadOnlyGuard ReadOnlyGuard { get; }
    public MetadataProvider MetadataProvider { get; }
    public SchemaService SchemaService { get; }
    public QueryExecutionService QueryExecutionService { get; }
    public QueryValidationService QueryValidationService { get; }
    public Anonymizer Anonymizer { get; }
    public AnonymizerExclusionProvider AnonymizerExclusionProvider { get; }

    public SqlServerFixture()
    {


        // Resolve appsettings.json next to the running test assembly. The file is copied into
        // the test output by the src project so the relative path is stable.
        string appsettingsPath = LocateAppsettings();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(appsettingsPath)!)
            .AddJsonFile(Path.GetFileName(appsettingsPath), optional: false, reloadOnChange: false)
            .Build();

        // If the user (or CI) has set SQLTOAI_CONNECTION_STRING, that takes precedence — same
        // behavior as the runtime. For local dev against the bundled appsettings.json no env var
        // is required.
        Options = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>() ?? new SqlToAiOptions();
        ConfigurationResolver.Resolve(Options);

        var optionsWrapper = Microsoft.Extensions.Options.Options.Create(Options);
        ConnectionFactory   = new SqlConnectionFactory(optionsWrapper);
        SecurityGuard       = new SecurityGuard(optionsWrapper);
        AccessLevelProvider = new AccessLevelProvider(ConnectionFactory, optionsWrapper, NullLogger<AccessLevelProvider>.Instance);
        AnonymizerExclusionProvider = new AnonymizerExclusionProvider(ConnectionFactory, optionsWrapper, NullLogger<AnonymizerExclusionProvider>.Instance);
        ReadOnlyGuard       = new ReadOnlyGuard();
        MetadataProvider    = new MetadataProvider(ConnectionFactory, optionsWrapper, NullLogger<MetadataProvider>.Instance);
        SchemaService       = new SchemaService(ConnectionFactory, SecurityGuard, AccessLevelProvider, MetadataProvider, optionsWrapper, NullLogger<SchemaService>.Instance);
        QueryExecutionService = new QueryExecutionService(ConnectionFactory, SecurityGuard, AccessLevelProvider, ReadOnlyGuard, new AnonymizationDependencies(new Anonymizer(optionsWrapper), AnonymizerExclusionProvider), optionsWrapper, NullLogger<QueryExecutionService>.Instance);
        QueryValidationService = new QueryValidationService(ConnectionFactory, SecurityGuard, AccessLevelProvider, optionsWrapper, NullLogger<QueryValidationService>.Instance);
        Anonymizer          = new Anonymizer(optionsWrapper);
    }

    private static string LocateAppsettings()
    {
        // Walk up from the test bin directory to find src/SqlToAi/appsettings.json. This is robust
        // regardless of whether the test runs from net10.0/ or from a different output layout.
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, "appsettings.json");
            if (File.Exists(candidate)) return candidate;

            string srcCandidate = Path.Combine(dir, "src", "SqlToAi", "appsettings.json");
            if (File.Exists(srcCandidate)) return srcCandidate;

            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Could not locate appsettings.json for integration tests.");
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
