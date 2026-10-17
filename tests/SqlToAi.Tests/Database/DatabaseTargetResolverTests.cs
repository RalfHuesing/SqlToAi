using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace SqlToAi.Tests.Database;

public sealed class DatabaseTargetResolverTests
{
    [Fact]
    public void ParseAndResolve_CanonicalizesOnlyAliasAndPreservesRawDatabaseName()
    {
        var options = new SqlToAiOptions();
        options.Servers["TEST"] = new NamedServerOptions();
        var resolver = new DatabaseTargetResolver(options);

        Result<ParsedDatabaseTarget> parsed = DatabaseTargetParser.Parse("TEST/App/Db/Å ");
        Assert.True(parsed.IsSuccess);
        Assert.Equal("test", parsed.Value.ServerAlias);
        Assert.Equal("App/Db/Å ", parsed.Value.DatabaseName);

        Result<DatabaseTargetContext> result = resolver.Resolve(parsed.Value);
        Assert.True(result.IsSuccess);
        Assert.Equal("test", result.Value.ServerAlias);
        Assert.Equal("App/Db/Å ", result.Value.DatabaseName);
        Assert.Equal("test/App/Db/Å ", result.Value.QualifiedName);
    }

    [Fact]
    public void ParseAndResolve_UsesOnlyExplicitAliasesIncludingDefaultAndDuplicateEndpoints()
    {
        var options = new SqlToAiOptions();
        var first = new NamedServerOptions { Server = "same-host" };
        var second = new NamedServerOptions { Server = "same-host" };
        options.Servers.Add("default", first);
        options.Servers.Add("other", second);
        var resolver = new DatabaseTargetResolver(options);

        Result<DatabaseTargetContext> defaultTarget = resolver.Resolve("default/AppDb");
        Assert.True(defaultTarget.IsSuccess);
        Assert.Equal("default/AppDb", defaultTarget.Value.QualifiedName);
        Assert.True(resolver.TryGetServerOptions("OTHER", out NamedServerOptions? resolvedOptions));
        Assert.Same(second, resolvedOptions);
        Assert.NotSame(first, resolvedOptions);
        Assert.True(resolver.Resolve("missing/AppDb").IsFailure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AppDb")]
    [InlineData("/AppDb")]
    [InlineData("test/")]
    [InlineData("9test/AppDb")]
    [InlineData("téšt/AppDb")]
    [InlineData("test/AppDb")]
    public void Resolve_InvalidOrUnknownTargetsUseInvalidParameters(string? target)
    {
        var resolver = new DatabaseTargetResolver(new SqlToAiOptions());

        Result<DatabaseTargetContext> result = resolver.Resolve(target);

        Assert.True(result.IsFailure);
        Assert.Equal("SQL-AI-0001", result.Error.Code);
    }

    [Fact]
    public void ContextKeepsOrdinalIdentityAndUsesIgnoreCaseForPolicyDatabaseMatching()
    {
        var options = new SqlToAiOptions();
        options.Servers.Add("test", new NamedServerOptions());
        options.Servers.Add("other", new NamedServerOptions());
        var resolver = new DatabaseTargetResolver(options);
        DatabaseTargetContext first = resolver.Resolve("test/AppDb").Value;
        DatabaseTargetContext policyMatch = resolver.Resolve("test/appdb").Value;

        Assert.NotEqual(first, policyMatch);
        Assert.True(first.HasSamePolicyIdentity(policyMatch));
        Assert.False(first.HasSamePolicyIdentity(resolver.Resolve("other/AppDb").Value));
    }

    [Theory]
    [InlineData("test")]
    [InlineData("test_")]
    [InlineData("test-2")]
    [InlineData("A9")]
    public void AliasValidationAndCanonicalizationAreAsciiInvariant(string alias)
    {
        Assert.True(DatabaseTargetParser.IsValidAlias(alias));
        Assert.Equal(alias.ToLowerInvariant(), DatabaseTargetParser.CanonicalizeAlias(alias));
    }

    [Fact]
    public void ResolverRejectsMalformedAndCaseDuplicateConfiguredAliases()
    {
        var malformed = new SqlToAiOptions();
        malformed.Servers.Add("bad.alias", new NamedServerOptions());
        Assert.Throws<ArgumentException>(() => new DatabaseTargetResolver(malformed));

        var duplicates = new SqlToAiOptions { Servers = new Dictionary<string, NamedServerOptions>(StringComparer.Ordinal) };
        duplicates.Servers.Add("test", new NamedServerOptions());
        duplicates.Servers.Add("TEST", new NamedServerOptions());
        Assert.Throws<ArgumentException>(() => new DatabaseTargetResolver(duplicates));
    }

    [Fact]
    public void NamedServerSettingsBindAsPerAliasModelsWithProvidersDisabledByDefault()
    {
        const string json = """
        {
          "SqlToAi": {
            "Servers": {
              "Prod": {
                "Server": "sql.example.invalid",
                "IntegratedSecurity": true,
                "ConnectTimeoutSeconds": 12,
                "ExcludedDatabases": ["master"],
                "MetadataProvider": { "Enabled": true },
                "AnonymizationRules": { "TableName": "security.Rules" }
              },
              "offline": { "Server": "" }
            }
          }
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        SqlToAiOptions options = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build()
            .GetSection("SqlToAi")
            .Get<SqlToAiOptions>()!;

        Assert.Equal("sql.example.invalid", options.Servers["Prod"].Server);
        Assert.True(options.Servers["Prod"].IntegratedSecurity);
        Assert.Equal(12, options.Servers["Prod"].ConnectTimeoutSeconds);
        Assert.Equal("master", Assert.Single(options.Servers["Prod"].ExcludedDatabases));
        Assert.True(options.Servers["Prod"].MetadataProvider.Enabled);
        Assert.Equal("security.Rules", options.Servers["Prod"].AnonymizationRules.TableName);
        Assert.False(options.Servers["offline"].MetadataProvider.Enabled);
        Assert.False(options.Servers["offline"].AnonymizationRules.Enabled);
    }
}
