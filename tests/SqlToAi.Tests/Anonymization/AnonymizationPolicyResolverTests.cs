#nullable enable

using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;

namespace SqlToAi.Tests.Anonymization;

// @covers SqlToAi.Anonymization.AnonymizationPolicyResolver
public sealed class AnonymizationPolicyResolverTests
{
    private static readonly Type TargetType = typeof(AnonymizationPolicyResolver);

    private static AnonymizationPolicyResolver BuildResolver(
        SqlToAiOptions? options = null,
        HashSet<string>? legacyExclusions = null,
        bool centralExcluded = false)
    {
        options ??= new SqlToAiOptions();
        var exclusionProvider = new FakeExclusionProvider(legacyExclusions ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var ruleProvider = new FakeRuleProvider(centralExcluded);
        return new AnonymizationPolicyResolver(Options.Create(options), exclusionProvider, ruleProvider);
    }

    [Fact]
    public async Task WillAnonymizeAsync_ShouldReturnFalse_WhenGloballyDisabled()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = false;
        var resolver = BuildResolver(options);

        bool result = await resolver.WillAnonymizeAsync("Db", "Table", "Column", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task WillAnonymizeAsync_ShouldReturnFalse_WhenColumnMatchesGlobExclusion()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.ExcludedColumns = new List<string> { "*Id" };
        var resolver = BuildResolver(options);

        bool result = await resolver.WillAnonymizeAsync("Db", "Table", "CustomerId", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task WillAnonymizeAsync_ShouldReturnFalse_WhenLegacyExclusionMatches()
    {
        var legacy = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FakeProjects.ProjectName" };
        var resolver = BuildResolver(legacyExclusions: legacy);

        bool result = await resolver.WillAnonymizeAsync("Db", "FakeProjects", "ProjectName", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task WillAnonymizeAsync_ShouldReturnFalse_WhenCentralRuleExcludes()
    {
        var resolver = BuildResolver(centralExcluded: true);

        bool result = await resolver.WillAnonymizeAsync("Db", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task WillAnonymizeAsync_ShouldReturnTrue_WhenNothingExcludesTheColumn()
    {
        var resolver = BuildResolver();

        bool result = await resolver.WillAnonymizeAsync("Db", "Customers", "LastName", TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // Tests: IsTokenizationActive
    // -------------------------------------------------------------------------

    [Fact]
    public void IsTokenizationActive_ShouldBeFalse_WhenTokenizationDisabled()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Tokenization.Enabled = false;
        options.Anonymizer.Tokenization.Secret = "top-secret";
        var resolver = BuildResolver(options);

        Assert.False(resolver.IsTokenizationActive);
    }

    [Fact]
    public void IsTokenizationActive_ShouldBeFalse_WhenSecretMissing()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Tokenization.Enabled = true;
        options.Anonymizer.Tokenization.Secret = "";
        var resolver = BuildResolver(options);

        Assert.False(resolver.IsTokenizationActive);
    }

    [Fact]
    public void IsTokenizationActive_ShouldBeTrue_WhenEnabledWithSecret()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Tokenization.Enabled = true;
        options.Anonymizer.Tokenization.Secret = "top-secret";
        var resolver = BuildResolver(options);

        Assert.True(resolver.IsTokenizationActive);
    }

    private sealed class FakeExclusionProvider(HashSet<string> exclusions) : IAnonymizerExclusionProvider
    {
        public Task<HashSet<string>> GetExclusionsAsync(string databaseName, CancellationToken cancellationToken = default)
            => Task.FromResult(exclusions);
    }

    private sealed class FakeRuleProvider(bool excluded) : IAnonymizationRuleProvider
    {
        public Task<bool> IsExcludedAsync(string databaseName, string tableName, string columnName, CancellationToken cancellationToken = default)
            => Task.FromResult(excluded);
    }
}
