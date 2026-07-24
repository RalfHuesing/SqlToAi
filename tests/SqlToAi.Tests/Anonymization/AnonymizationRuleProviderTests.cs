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

        bool excluded = await provider.IsExcludedAsync("AnyDb", "AnySchema", "AnyTable", "AnyColumn", TestContext.Current.CancellationToken);

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

        bool excluded = await provider.IsExcludedAsync("AnyDb", string.Empty, "OtherTable", "OtherColumn", TestContext.Current.CancellationToken);

        Assert.False(excluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldExclude_WhenWildcardRuleAllowsWholeTable()
    {
        var options = BuildOptions();
        var rows = new List<RuleRowData> { new("%", "FakeConsultants", "%", false) };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("AnyDb", string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

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

        bool nameExcluded = await provider.IsExcludedAsync("AnyDb", string.Empty, "FakeConsultants", "FullName", TestContext.Current.CancellationToken);
        bool phoneExcluded = await provider.IsExcludedAsync("AnyDb", string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

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

        bool emailExcluded = await provider.IsExcludedAsync("FakeHighSecurityDb", string.Empty, "Contacts", "ContactEmail", TestContext.Current.CancellationToken);
        bool otherExcluded = await provider.IsExcludedAsync("FakeHighSecurityDb", string.Empty, "Contacts", "Notes", TestContext.Current.CancellationToken);

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

        await provider.IsExcludedAsync("AnyDb", string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);
        Assert.Equal(1, factory.ConnectionCreatedCount);

        await provider.IsExcludedAsync("AnyDb", string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);
        Assert.Equal(1, factory.ConnectionCreatedCount); // still cached

        await Task.Delay(1100, TestContext.Current.CancellationToken);

        await provider.IsExcludedAsync("AnyDb", string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);
        Assert.Equal(2, factory.ConnectionCreatedCount); // reloaded after TTL
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenTableDoesNotExist()
    {
        var options = BuildOptions();
        var factory = new DummyConnectionFactory(new MockConnection([], simulatedTableName: null));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("AnyDb", string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.False(excluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenQueryThrows()
    {
        var options = BuildOptions();
        var factory = new DummyConnectionFactory(new MockConnection([], new MockConnectionFlags(ThrowException: true)));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("AnyDb", string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.False(excluded);
    }

    // -------------------------------------------------------------------------
    // Schema-scoped rules (audit finding — see
    // tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md, Finding "Ausschluss-/Regel-Abgleich
    // ist schema-blind — gleichnamige Tabelle in anderem Schema erbt fremde Freigabe"). Reproduces
    // the exact scenario: dbo.Kunden and Archiv.Kunden both have an Email column.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsExcludedAsync_ShouldIsolateSchemas_WhenRuleTableHasSchemaPatternColumn()
    {
        var options = BuildOptions();
        var rows = new List<RuleRowData> { new("%", "Kunden", "Email", false, SchemaPattern: "dbo") };
        var factory = new DummyConnectionFactory(new MockConnection(rows, new MockConnectionFlags(HasSchemaPatternColumn: true)));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool dboExcluded = await provider.IsExcludedAsync("AnyDb", "dbo", "Kunden", "Email", TestContext.Current.CancellationToken);
        bool archivExcluded = await provider.IsExcludedAsync("AnyDb", "Archiv", "Kunden", "Email", TestContext.Current.CancellationToken);

        // The rule is scoped to schema "dbo" only; the same-named table in schema "Archiv" must
        // stay anonymized (no matching rule for it).
        Assert.True(dboExcluded);
        Assert.False(archivExcluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldApplyToEverySchema_WhenRuleTableHasNoSchemaPatternColumn()
    {
        // Backward-compatibility regression: a rule table that hasn't run the migration adding
        // [SchemaPattern] must keep working with zero-config, schema-agnostic matching — the rule
        // keeps applying across every schema, exactly as before this column existed.
        var options = BuildOptions();
        var rows = new List<RuleRowData> { new("%", "Kunden", "Email", false) };
        var factory = new DummyConnectionFactory(new MockConnection(rows, new MockConnectionFlags(HasSchemaPatternColumn: false)));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool dboExcluded = await provider.IsExcludedAsync("AnyDb", "dbo", "Kunden", "Email", TestContext.Current.CancellationToken);
        bool archivExcluded = await provider.IsExcludedAsync("AnyDb", "Archiv", "Kunden", "Email", TestContext.Current.CancellationToken);

        Assert.True(dboExcluded);
        Assert.True(archivExcluded);
    }
}
