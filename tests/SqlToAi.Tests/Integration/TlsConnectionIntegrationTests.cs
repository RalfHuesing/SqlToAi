using System.Data.Common;
using System.IO;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Metadata;
using SqlToAi.Security;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "TlsIntegration")]
public sealed class TlsConnectionIntegrationTests(ITestOutputHelper output)
{
    private const string ObservationQuery = """
        SELECT CONVERT(nvarchar(10), encrypt_option) + '|' + net_transport
        FROM sys.dm_exec_connections WHERE session_id = @@SPID
        """;

    public static TheoryData<string, string, bool> Cases => new()
    {
        { "TrustedTcp", "Primary", false },
        { "TrustedTcp", "Metadata", false },
        { "TrustedTcp", "Rules", false },
        { "UntrustedTcp", "Primary", false },
        { "UntrustedTcp", "Metadata", false },
        { "UntrustedTcp", "Rules", false },
        { "UntrustedTcp", "Primary", true },
        { "UntrustedTcp", "Metadata", true },
        { "UntrustedTcp", "Rules", true },
        { "LocalUntrusted", "Primary", false },
        { "LocalUntrusted", "Metadata", false },
        { "LocalUntrusted", "Rules", false },
        { "LocalUntrusted", "Primary", true },
        { "LocalUntrusted", "Metadata", true },
        { "LocalUntrusted", "Rules", true }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EndpointPolicy_RejectsOrObservesEncryptedSession(string endpoint, string path, bool trust)
    {
        var (options, database) = LoadEndpoint(endpoint);
        bool rejected = endpoint != "TrustedTcp" && !trust;
        ConfigureProviders(options, database, trust, dedicated: true);
        // A primary exception must not relax a dedicated provider's default policy.
        options.Servers["test"].TrustServerCertificate = path == "Primary" ? trust : true;
        var factory = new CountingFactory(new SqlConnectionFactory(Options.Create(options)));
        var metadataLog = new TestLogger<MetadataProvider>();
        var rulesLog = new TestLogger<AnonymizationRuleProvider>();
        var metadata = new MetadataProvider(factory, Options.Create(options), metadataLog);
        var rules = new AnonymizationRuleProvider(factory, Options.Create(options), rulesLog);

        // Observe the connection constructed by each actual provider, without exposing
        // a production test hook or replacing its dedicated connection with a fake.
        var target = new DatabaseTargetContext("test", database);
        await using DbConnection connection = path switch
        {
            "Primary" => factory.CreateTargetConnection(target),
            "Metadata" => SecondaryConnectionBuilder.CreateForOwner(
                target, options.Servers["test"], options.Servers["test"].MetadataProvider, SecondaryConnectionBuilder.MetadataApplicationName, factory),
            _ => SecondaryConnectionBuilder.CreateForOwner(
                target, options.Servers["test"], options.Servers["test"].AnonymizationRules, SecondaryConnectionBuilder.AnonymizationRulesApplicationName, factory)
        };

        if (rejected)
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => connection.OpenAsync(TestContext.Current.CancellationToken));
            Assert.Contains("TLS/certificate", SqlToAiErrorMapper.GetSafeConnectionFailureCategory(error));
            Assert.Equal("SQL-AI-0105", SqlToAiErrorMapper.MapException(error).Code);
            output.WriteLine($"{endpoint}/{path}: trust=false, TLS/certificate rejection; no established session.");
        }
        else
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = ObservationQuery;
            string observation = Assert.IsType<string>(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
            Assert.StartsWith("TRUE|", observation, StringComparison.OrdinalIgnoreCase);
            if (endpoint.EndsWith("Tcp", StringComparison.Ordinal))
                Assert.Equal("TRUE|TCP", observation, ignoreCase: true);
            output.WriteLine($"{endpoint}/{path}: trust={trust}, provider-created session={observation}.");
        }

        if (path == "Metadata")
        {
            string? observation = await metadata.GetTableDescriptionAsync(target, "tls_probe", TestContext.Current.CancellationToken);
            if (rejected)
                Assert.Null(observation);
            else
            {
                Assert.NotNull(observation);
                Assert.StartsWith("TRUE|", observation, StringComparison.OrdinalIgnoreCase);
                if (endpoint.EndsWith("Tcp", StringComparison.Ordinal))
                    Assert.Equal("TRUE|TCP", observation, ignoreCase: true);
                output.WriteLine($"{endpoint}/Metadata: actual lookup session={observation}.");
            }
            VerifyLogs(metadataLog, rejected, "MetadataProvider");
        }
        if (path == "Rules")
        {
            Assert.False(await rules.IsExcludedAsync(target, "dbo", "tls_probe", "value", TestContext.Current.CancellationToken));
            VerifyLogs(rulesLog, rejected, "AnonymizationRules");
            output.WriteLine($"{endpoint}/Rules: real lookup returned default masking; its separate lookup session was not observed.");
        }
        Assert.Equal(path == "Primary" ? 1 : 0, factory.Count);
    }

    [Theory]
    [InlineData("LocalUntrusted")]
    [InlineData("UntrustedTcp")]
    public async Task PrimaryDefault_RealCertificateFailureReachesServiceError(string endpoint)
    {
        var (options, database) = LoadEndpoint(endpoint);
        options.Databases.ReadWrite = [$"test/{database}"];
        var wrapped = Options.Create(options);
        var factory = new CountingFactory(new SqlConnectionFactory(wrapped));
        var accessLog = new TestLogger<AccessLevelProvider>();
        var serviceLog = new TestLogger<QueryValidationService>();
        var safety = new QuerySafetyValidator(new SecurityGuard(wrapped),
            new AccessLevelProvider(factory, wrapped, accessLog), new ReadOnlyGuard());
        var service = new QueryValidationService(factory, safety, wrapped, serviceLog);
        var result = await service.ValidateQueryAsync($"test/{database}", "SELECT 1", TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal("SQL-AI-0105", result.Error.Code);
        Assert.Contains("SqlServer", result.Error.Message);
        Assert.Contains("TLS/certificate", result.Error.Message);
        Assert.Equal(1, factory.Count);
        Assert.NotEmpty(serviceLog.Entries);
        Assert.All(serviceLog.Entries, entry =>
        {
            Assert.NotNull(entry.Exception);
            Assert.Null(entry.Exception.InnerException);
            Assert.DoesNotContain("Password=", entry.Exception.ToString(), StringComparison.OrdinalIgnoreCase);
        });
        output.WriteLine($"{endpoint}/Primary: real validation service returned SQL-AI-0105; trust=false, no retry.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalFallback_UsesPrimaryPolicyDespiteOppositeSecondarySetting(bool primaryTrust)
    {
        var (options, database) = LoadEndpoint("LocalUntrusted");
        ConfigureProviders(options, database, !primaryTrust, dedicated: false);
        options.Servers["test"].TrustServerCertificate = primaryTrust;
        var factory = new CountingFactory(new SqlConnectionFactory(Options.Create(options)));
        var metadataLog = new TestLogger<MetadataProvider>();
        var rulesLog = new TestLogger<AnonymizationRuleProvider>();
        var metadata = new MetadataProvider(factory, Options.Create(options), metadataLog);
        var rules = new AnonymizationRuleProvider(factory, Options.Create(options), rulesLog);
        var target = new DatabaseTargetContext("test", database);
        string? observation = await metadata.GetTableDescriptionAsync(target, "tls_probe", TestContext.Current.CancellationToken);
        if (primaryTrust)
        {
            Assert.NotNull(observation);
            Assert.StartsWith("TRUE|", observation, StringComparison.OrdinalIgnoreCase);
            output.WriteLine($"LocalUntrusted/Metadata fallback: primary trust=true, actual lookup session={observation}.");
        }
        else
            Assert.Null(observation);
        Assert.False(await rules.IsExcludedAsync(target, "dbo", "tls_probe", "value", TestContext.Current.CancellationToken));
        VerifyLogs(metadataLog, !primaryTrust, "MetadataProvider");
        VerifyLogs(rulesLog, !primaryTrust, "AnonymizationRules");
        Assert.Equal(2, factory.Count);
    }

    private static (SqlToAiOptions Options, string Database) LoadEndpoint(string name)
    {
        string? profile = Environment.GetEnvironmentVariable("SQLTOAI_TLS_TEST_PROFILE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(profile), "Unverified: SQLTOAI_TLS_TEST_PROFILE is not explicitly configured.");
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(profile!), optional: false).Build();
        var section = configuration.GetSection(name);
        Assert.SkipWhen(!section.Exists(), $"Unverified: {name} endpoint is unavailable.");
        var serverSection = section.GetSection("Servers:test").Exists()
            ? section.GetSection("Servers:test")
            : (section.GetSection("Servers:primary").Exists()
                ? section.GetSection("Servers:primary")
                : section.GetSection("SqlServer"));
        var serverOptions = serverSection.Get<NamedServerOptions>() ?? new NamedServerOptions();
        var options = new SqlToAiOptions();
        options.Servers["test"] = serverOptions;
        Assert.NotNull(options.Servers["test"]);
        Assert.False(options.Servers["test"].TrustServerCertificate, "TLS inputs must retain secure defaults; each test selects its own exception.");
        Assert.False(string.IsNullOrWhiteSpace(options.Servers["test"].Server));
        if (name.EndsWith("Tcp", StringComparison.Ordinal))
            Assert.StartsWith("tcp:", options.Servers["test"].Server, StringComparison.OrdinalIgnoreCase);
        string database = section["Database"] ?? "DemoDB";
        return (options, database);
    }

    private static void ConfigureProviders(SqlToAiOptions options, string database, bool trust, bool dedicated)
    {
        var primary = options.Servers["test"];
        primary.MetadataProvider.Enabled = true;
        primary.MetadataProvider.Server = dedicated ? primary.Server : string.Empty;
        primary.MetadataProvider.Database = database;
        primary.MetadataProvider.IntegratedSecurity = primary.IntegratedSecurity;
        primary.MetadataProvider.UserId = primary.UserId;
        primary.MetadataProvider.Password = primary.Password;
        primary.MetadataProvider.TrustServerCertificate = trust;
        primary.MetadataProvider.CommandTimeoutSeconds = primary.ConnectTimeoutSeconds;
        primary.MetadataProvider.TableMetadataQuery = ObservationQuery;
        primary.AnonymizationRules.Enabled = true;
        primary.AnonymizationRules.Server = dedicated ? primary.Server : string.Empty;
        primary.AnonymizationRules.Database = database;
        primary.AnonymizationRules.IntegratedSecurity = primary.IntegratedSecurity;
        primary.AnonymizationRules.UserId = primary.UserId;
        primary.AnonymizationRules.Password = primary.Password;
        primary.AnonymizationRules.TrustServerCertificate = trust;
        primary.AnonymizationRules.CommandTimeoutSeconds = primary.ConnectTimeoutSeconds;
        // A missing fictional table tests successful optional loading without changing SQL objects.
        primary.AnonymizationRules.TableName = "dbo.SqlToAiTlsProbeNotPresent";
    }

    private static void VerifyLogs<T>(TestLogger<T> logger, bool rejected, string section)
    {
        var errors = logger.Entries.Where(entry => entry.Level >= LogLevel.Warning).ToList();
        if (rejected)
        {
            Assert.NotEmpty(errors);
            Assert.All(errors, entry =>
            {
                Assert.Contains(section, entry.Message);
                Assert.Contains("TLS/certificate", entry.Message);
                Assert.Null(entry.Exception);
            });
        }
        else
            Assert.Empty(errors);
    }

    private sealed class CountingFactory(IDatabaseConnectionFactory inner) : IDatabaseConnectionFactory
    {
        public System.Data.Common.DbConnection CreateTargetConnection(SqlToAi.Domain.DatabaseTargetContext target)
        {
            Count++;
            return inner.CreateTargetConnection(target);
        }

        public SqlToAi.Domain.Result<System.Data.Common.DbConnection> CreateCatalogConnection(string? serverAlias) => throw new NotSupportedException();

        public int Count { get; private set; }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
