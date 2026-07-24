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

    // -------------------------------------------------------------------------
    // Rule-precedence scoring (audit finding — see
    // tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md, Finding "Regel-Präzedenz gewichtet
    // Datenbank- vor Spalten-Spezifität — breite DB-Regel kann gezielten Spalten-Schutz aushebeln").
    // The old weighted-sum scoring (DB*1000 + Schema*100 + Table*10 + Column) let a rule that was
    // merely specific about the database dominate a rule that was exactly specific about the column.
    // Pareto-dominance ranking with a protective tie-break replaces it.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsExcludedAsync_ShouldKeepColumnProtected_WhenIncomparableWithBroadDatabaseRule()
    {
        // Exact reproduction of the audit's Finding 3 scenario:
        // Rule A: DB=%, Schema=%, Table=%, Column=SSN, Anonymize=true   -> meant to protect SSN everywhere.
        // Rule B: DB=StagingDB, Schema=%, Table=%, Column=%, Anonymize=false -> meant to unlock one staging DB broadly.
        // Neither dominates the other (A is more specific in Column, B is more specific in Database).
        // Under the old weighted sum, B won (2000 > 2) and SSN leaked in StagingDB. The fix must
        // prefer the protective rule A when rules are genuinely incomparable.
        var options = BuildOptions();
        var rows = new List<RuleRowData>
        {
            new("%", "%", "SSN", true),
            new("StagingDB", "%", "%", false),
        };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync("StagingDB", "dbo", "Mitarbeiter", "SSN", TestContext.Current.CancellationToken);

        Assert.False(excluded); // must stay anonymized -> NOT excluded, despite the broad StagingDB rule
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldPickMoreSpecificRule_WhenOneDominatesInEveryDimension()
    {
        // Unambiguous case: the FullName-specific rule is at least as specific as the wildcard rule
        // in every dimension and strictly more specific in Column, so it dominates and must win,
        // exactly as it did under the old weighted-sum scoring (no regression for the clear-cut case).
        var options = BuildOptions();
        var rows = new List<RuleRowData>
        {
            new("%", "FakeConsultants", "%", false),
            new("%", "FakeConsultants", "FullName", true),
        };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool nameExcluded = await provider.IsExcludedAsync("AnyDb", string.Empty, "FakeConsultants", "FullName", TestContext.Current.CancellationToken);

        Assert.False(nameExcluded); // the dominating, more specific rule wins -> stays anonymized
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldTieBreakDeterministically_WhenBothIncomparableRulesAreProtective()
    {
        // Two mutually non-dominated rules, both protective (Anonymize=true): rule C is exact on
        // Column but wildcard on Database, rule D is exact on Database but wildcard on Column.
        // Neither dominates and the "prefer protective" tie-break can't discriminate (both are
        // protective), so the deterministic last-resort weighted-sum tie-break decides. Its exact
        // choice carries no security meaning; this test only pins down that the pick is stable and
        // does not crash. Rule D's weighted sum (2000) exceeds rule C's (2), so D wins.
        var options = BuildOptions();
        var rows = new List<RuleRowData>
        {
            new("%", "%", "Foo", true),
            new("SpecificDb", "%", "%", true),
        };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excludedFoo = await provider.IsExcludedAsync("SpecificDb", "dbo", "AnyTable", "Foo", TestContext.Current.CancellationToken);

        Assert.False(excludedFoo); // both candidates are protective, so either pick keeps it anonymized
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldTieBreakDeterministically_WhenBothIncomparableRulesArePermissive()
    {
        // Same shape as above, but both rules are permissive (Anonymize=false). Neither dominates,
        // and there is no protective rule to prefer, so the deterministic weighted-sum tie-break
        // decides between two equally-permissive candidates. The exact winner has no security
        // meaning (both would unblock the column); this only asserts a stable, non-crashing result.
        var options = BuildOptions();
        var rows = new List<RuleRowData>
        {
            new("%", "%", "Foo", false),
            new("SpecificDb", "%", "%", false),
        };
        var factory = new DummyConnectionFactory(new MockConnection(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excludedFoo = await provider.IsExcludedAsync("SpecificDb", "dbo", "AnyTable", "Foo", TestContext.Current.CancellationToken);

        Assert.True(excludedFoo); // both candidates are permissive, so either pick allows raw output
    }
}
