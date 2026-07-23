#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;

namespace SqlToAi.Tests.Anonymization;

// @covers SqlToAi.Anonymization.AnonymizationRuleProvider
// @covers SqlToAi.Anonymization.RuleCacheEntry
// @covers SqlToAi.Anonymization.AnonymizationRule
public sealed class AnonymizationRuleProviderTests
{
    private static readonly Type TargetType = typeof(AnonymizationRuleProvider);

    private static SqlToAiOptions BuildOptions(bool enabled = true, int cacheTtlSeconds = 300)
    {
        var options = new SqlToAiOptions();
        options.AnonymizationRules.Enabled = enabled;
        options.AnonymizationRules.TableName = "dbo.AnonymizationRules";
        options.AnonymizationRules.CacheTtlSeconds = cacheTtlSeconds;
        return options;
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenDisabled()
    {
        var options = BuildOptions(enabled: false);
        var factory = new DummyConnectionFactory();
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("AnyDb", "AnyTable", "AnyColumn", TestContext.Current.CancellationToken);

        Assert.False(excluded);
        Assert.Equal(0, factory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenNoRuleMatches()
    {
        var options = BuildOptions();
        var rows = new List<RuleRowData> { new("%", "FakeConsultants", "%", false) };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("AnyDb", "OtherTable", "OtherColumn", TestContext.Current.CancellationToken);

        Assert.False(excluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldExclude_WhenWildcardRuleAllowsWholeTable()
    {
        var options = BuildOptions();
        var rows = new List<RuleRowData> { new("%", "FakeConsultants", "%", false) };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.True(excluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldPreferMoreSpecificRule_ThatReAnonymizesOneColumn()
    {
        // Regression scenario from the design discussion: a broad "allow whole table" rule
        // combined with a specific "except this one column" override.
        var options = BuildOptions();
        var rows = new List<RuleRowData>
        {
            new("%", "FakeConsultants", "%", false),
            new("%", "FakeConsultants", "FullName", true)
        };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool nameExcluded = await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "FullName", TestContext.Current.CancellationToken);
        bool phoneExcluded = await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.False(nameExcluded); // more specific rule wins -> stays anonymized
        Assert.True(phoneExcluded); // falls back to the wildcard rule -> allowed raw
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldSupportAllowListOnlyDatabase()
    {
        // A database with no broad wildcard rule stays fully anonymized except for explicit allows.
        var options = BuildOptions();
        var rows = new List<RuleRowData> { new("FakeHighSecurityDb", "%", "ContactEmail", false) };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool emailExcluded = await provider.IsExcludedAsync("FakeHighSecurityDb", "Contacts", "ContactEmail", TestContext.Current.CancellationToken);
        bool otherExcluded = await provider.IsExcludedAsync("FakeHighSecurityDb", "Contacts", "Notes", TestContext.Current.CancellationToken);

        Assert.True(emailExcluded);
        Assert.False(otherExcluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldCacheRules_AndReloadAfterTtlExpires()
    {
        var options = BuildOptions(cacheTtlSeconds: 1);
        var rows = new List<RuleRowData> { new("%", "FakeConsultants", "%", false) };
        var mockConn = new MockConnection(rows);
        var factory = new DummyConnectionFactory(mockConn);
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);
        Assert.Equal(1, factory.ConnectionCreatedCount);

        await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);
        Assert.Equal(1, factory.ConnectionCreatedCount); // still cached

        await Task.Delay(1100, TestContext.Current.CancellationToken);

        await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);
        Assert.Equal(2, factory.ConnectionCreatedCount); // reloaded after TTL
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenTableDoesNotExist()
    {
        var options = BuildOptions();
        var factory = new DummyConnectionFactory(new MockConnection([], simulatedTableName: null));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.False(excluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenQueryThrows()
    {
        var options = BuildOptions();
        var factory = new DummyConnectionFactory(new MockConnection([], throwException: true));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("AnyDb", "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.False(excluded);
    }

    // -------------------------------------------------------------------------
    // Tests: IsSearchableTokenAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsSearchableTokenAsync_ShouldReturnFalse_WhenDisabled()
    {
        var options = BuildOptions(enabled: false);
        var factory = new DummyConnectionFactory();
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool searchable = await provider.IsSearchableTokenAsync("AnyDb", "AnyTable", "AnyColumn", TestContext.Current.CancellationToken);

        Assert.False(searchable);
    }

    [Fact]
    public async Task IsSearchableTokenAsync_ShouldReturnFalse_WhenNoRuleMatches()
    {
        var options = BuildOptions();
        var rows = new List<RuleRowData> { new("%", "FakeAccounts", "%", true, SearchableToken: true) };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool searchable = await provider.IsSearchableTokenAsync("AnyDb", "OtherTable", "OtherColumn", TestContext.Current.CancellationToken);

        Assert.False(searchable);
    }

    [Fact]
    public async Task IsSearchableTokenAsync_ShouldReturnTrue_WhenMostSpecificRuleFlagsColumn()
    {
        var options = BuildOptions();
        var rows = new List<RuleRowData>
        {
            new("%", "FakeAccounts", "%", true),
            new("%", "FakeAccounts", "IBAN", true, SearchableToken: true)
        };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool ibanSearchable = await provider.IsSearchableTokenAsync("AnyDb", "FakeAccounts", "IBAN", TestContext.Current.CancellationToken);
        bool ownerSearchable = await provider.IsSearchableTokenAsync("AnyDb", "FakeAccounts", "OwnerName", TestContext.Current.CancellationToken);

        Assert.True(ibanSearchable);
        Assert.False(ownerSearchable); // falls back to the wildcard rule, which is not searchable
    }

}
