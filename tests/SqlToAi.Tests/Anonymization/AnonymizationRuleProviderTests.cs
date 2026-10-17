using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlToAi.Tests.Database;
using SqlToAi.Tests.TestSupport;
using System.Net;
using System.Net.Sockets;

namespace SqlToAi.Tests.Anonymization;

public sealed class AnonymizationRuleProviderTests
{
    private static readonly Type TargetType = typeof(AnonymizationRuleProvider);
    private const string ServerAlias = "primary";
    private static DatabaseTargetContext Target(string dbName) => new(ServerAlias, dbName);

    private static SqlToAiOptions BuildOptions(bool enabled = true, int cacheTtlSeconds = 300)
    {
        var options = new SqlToAiOptions().WithNamedServers(ServerAlias);
        options.Servers[ServerAlias].AnonymizationRules.Enabled = enabled;
        options.Servers[ServerAlias].AnonymizationRules.TableName = "dbo.AnonymizationRules";
        options.Servers[ServerAlias].AnonymizationRules.CacheTtlSeconds = cacheTtlSeconds;
        return options;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsExcludedAsync_RequestedCancellationDoesNotCacheFallback(bool duringOpen)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var queries = new List<string>();
        var connection = duringOpen
            ? new FakeDbConnection(_ => throw new NotSupportedException(),
                new FakeDbConnectionOptions(OpenFailure: new OperationCanceledException(cancellation.Token)))
            : MockConnection.Create([], beforeExecute: command =>
            {
                queries.Add(command.CommandText);
                if (command.CommandText.Contains("COL_LENGTH", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(cancellation.Token);
                }
            });
        var factory = new DummyConnectionFactory(connection);
        var provider = new AnonymizationRuleProvider(factory, Options.Create(BuildOptions()), NullLogger<AnonymizationRuleProvider>.Instance);
        if (duringOpen) await cancellation.CancelAsync();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.IsExcludedAsync(Target("AnyDb"), "dbo", "Customers", "Name", cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.DoesNotContain(queries, query => query.Contains("WHERE [IsActive]", StringComparison.Ordinal));
        factory.ConnectionToReturn = MockConnection.Create([new("%", "Customers", "Name", false)]);
        Assert.True(await provider.IsExcludedAsync(Target("AnyDb"), "dbo", "Customers", "Name", TestContext.Current.CancellationToken));
        Assert.True(await provider.IsExcludedAsync(Target("AnyDb"), "dbo", "Customers", "Name", TestContext.Current.CancellationToken));
        Assert.Equal(2, factory.ConnectionCreatedCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsExcludedAsync_UnrequestedCancellationKeepsFallbackAndCachesIt(bool duringOpen)
    {
        var connection = duringOpen
            ? new FakeDbConnection(_ => throw new NotSupportedException(),
                new FakeDbConnectionOptions(OpenFailure: new OperationCanceledException()))
            : MockConnection.Create([new("%", "Customers", "Name", false)], beforeExecute: command =>
            {
                if (command.CommandText.Contains("COL_LENGTH", StringComparison.Ordinal))
                    throw new OperationCanceledException();
            });
        var factory = new DummyConnectionFactory(connection);
        var provider = new AnonymizationRuleProvider(factory, Options.Create(BuildOptions()), NullLogger<AnonymizationRuleProvider>.Instance);

        bool first = await provider.IsExcludedAsync(Target("AnyDb"), "dbo", "Customers", "Name", TestContext.Current.CancellationToken);
        bool second = await provider.IsExcludedAsync(Target("AnyDb"), "dbo", "Customers", "Name", TestContext.Current.CancellationToken);

        Assert.Equal(!duringOpen, first);
        Assert.Equal(first, second);
        Assert.Equal(1, factory.ConnectionCreatedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateConnection_ShouldUseBoundRuleTrustSetting_IndependentlyOfPrimary(bool trustRulesCertificate)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SqlToAi:Servers:primary:Server"] = "sql-primary",
                ["SqlToAi:Servers:primary:TrustServerCertificate"] = (!trustRulesCertificate).ToString(),
                ["SqlToAi:Servers:primary:AnonymizationRules:Server"] = "rules-server",
                ["SqlToAi:Servers:primary:AnonymizationRules:Database"] = "RulesDb",
                ["SqlToAi:Servers:primary:AnonymizationRules:IntegratedSecurity"] = "true",
                ["SqlToAi:Servers:primary:AnonymizationRules:TrustServerCertificate"] = trustRulesCertificate.ToString()
            })
            .Build();
        var options = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>()!;
        var server = options.Servers["primary"];
        var target = new DatabaseTargetContext("primary", "RulesDb");
        using var connection = (SqlConnection)SecondaryConnectionBuilder.CreateForOwner(
            target, server, server.AnonymizationRules, SecondaryConnectionBuilder.AnonymizationRulesApplicationName, new DummyConnectionFactory());

        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal("RulesDb", builder.InitialCatalog);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, builder.Encrypt);
        Assert.Equal(trustRulesCertificate, builder.TrustServerCertificate);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenDisabled()
    {
        var options = BuildOptions(enabled: false);
        var factory = new DummyConnectionFactory();
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync(Target("AnyDb"), "AnySchema", "AnyTable", "AnyColumn", TestContext.Current.CancellationToken);

        Assert.False(excluded);
        Assert.Equal(0, factory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenNoRuleMatches()
    {
        var options = BuildOptions();
        var rows = new List<RuleRowData> { new("%", "FakeConsultants", "%", false) };
        var factory = new DummyConnectionFactory(MockConnection.Create(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync(Target("AnyDb"), string.Empty, "OtherTable", "OtherColumn", TestContext.Current.CancellationToken);

        Assert.False(excluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldExclude_WhenWildcardRuleAllowsWholeTable()
    {
        var options = BuildOptions();
        var rows = new List<RuleRowData> { new("%", "FakeConsultants", "%", false) };
        var factory = new DummyConnectionFactory(MockConnection.Create(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync(Target("AnyDb"), string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

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
        var factory = new DummyConnectionFactory(MockConnection.Create(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool nameExcluded = await provider.IsExcludedAsync(Target("AnyDb"), string.Empty, "FakeConsultants", "FullName", TestContext.Current.CancellationToken);
        bool phoneExcluded = await provider.IsExcludedAsync(Target("AnyDb"), string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.False(nameExcluded); // more specific rule wins -> stays anonymized
        Assert.True(phoneExcluded); // falls back to the wildcard rule -> allowed raw
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldSupportAllowListOnlyDatabase()
    {
        // A database with no broad wildcard rule stays fully anonymized except for explicit allows.
        var options = BuildOptions();
        var rows = new List<RuleRowData> { new("FakeHighSecurityDb", "%", "ContactEmail", false) };
        var factory = new DummyConnectionFactory(MockConnection.Create(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool emailExcluded = await provider.IsExcludedAsync(Target("FakeHighSecurityDb"), string.Empty, "Contacts", "ContactEmail", TestContext.Current.CancellationToken);
        bool otherExcluded = await provider.IsExcludedAsync(Target("FakeHighSecurityDb"), string.Empty, "Contacts", "Notes", TestContext.Current.CancellationToken);

        Assert.True(emailExcluded);
        Assert.False(otherExcluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldCacheRules_AndReloadAfterTtlExpires()
    {
        var options = BuildOptions(cacheTtlSeconds: 1);
        var rows = new List<RuleRowData> { new("%", "FakeConsultants", "%", false) };
        var mockConn = MockConnection.Create(rows);
        var factory = new DummyConnectionFactory(mockConn);
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        await provider.IsExcludedAsync(Target("AnyDb"), string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);
        Assert.Equal(1, factory.ConnectionCreatedCount);

        await provider.IsExcludedAsync(Target("AnyDb"), string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);
        Assert.Equal(1, factory.ConnectionCreatedCount); // still cached

        await Task.Delay(1100, TestContext.Current.CancellationToken);

        await provider.IsExcludedAsync(Target("AnyDb"), string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);
        Assert.Equal(2, factory.ConnectionCreatedCount); // reloaded after TTL
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenTableDoesNotExist()
    {
        var options = BuildOptions();
        var factory = new DummyConnectionFactory(MockConnection.Create([], simulatedTableName: null));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync(Target("AnyDb"), string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.False(excluded);
    }

    [Fact]
    public async Task IsExcludedAsync_ShouldReturnFalse_WhenQueryThrows()
    {
        var options = BuildOptions();
        var factory = new DummyConnectionFactory(MockConnection.Create([], new MockConnectionFlags(ThrowException: true)));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync(Target("AnyDb"), string.Empty, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.False(excluded);
    }

    [Fact]
    public async Task IsExcludedAsync_TlsOpenFailureKeepsDefaultMaskingAndCachesEmptyRulesSafely()
    {
        const string marker = "synthetic-secret-marker";
        var failure = SqlExceptionFactory.Create(unchecked((int)0x80090325),
            $"Untrusted certificate Password={marker};Server=raw-connection-marker;", severity: 20,
            inner: new InvalidOperationException($"inner {marker};Server=raw-connection-marker;"));
        var connection = new FakeDbConnection(_ => throw new NotSupportedException(), new FakeDbConnectionOptions(OpenFailure: failure));
        var factory = new DummyConnectionFactory(connection);
        var logger = new CapturingLogger<AnonymizationRuleProvider>();
        var provider = new AnonymizationRuleProvider(factory, Options.Create(BuildOptions()), logger);

        bool first = await provider.IsExcludedAsync(Target("AnyDb"), "dbo", "Customers", "Name", TestContext.Current.CancellationToken);
        bool second = await provider.IsExcludedAsync(Target("AnyDb"), "dbo", "Customers", "Name", TestContext.Current.CancellationToken);

        Assert.False(first);
        Assert.False(second);
        Assert.Equal(1, factory.ConnectionCreatedCount);
        Assert.Equal(1, connection.OpenCount);
        Assert.NotEmpty(logger.Entries);
        foreach (var (message, exception) in logger.Entries)
        {
            Assert.Contains("AnonymizationRules", message);
            Assert.Contains("TLS/certificate connection failure", message);
            Assert.Contains("default masking remains active", message);
            Assert.DoesNotContain(marker, message);
            Assert.DoesNotContain("raw-connection-marker", message);
            Assert.Null(exception);
        }
    }

    [Fact]
    public async Task IsExcludedAsync_DedicatedOpenFailureDoesNotRetryPrimaryAndCachesFallback()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task accepted = Task.Run(async () =>
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        var options = BuildOptions();
        options.Servers[ServerAlias].AnonymizationRules.Server = $"127.0.0.1,{port}";
        options.Servers[ServerAlias].AnonymizationRules.Database = "RulesDb";
        options.Servers[ServerAlias].AnonymizationRules.UserId = "rules-user-marker";
        options.Servers[ServerAlias].AnonymizationRules.Password = "rules-password-marker";
        options.Servers[ServerAlias].AnonymizationRules.IntegratedSecurity = false;
        options.Servers[ServerAlias].AnonymizationRules.TrustServerCertificate = false;
        options.Servers[ServerAlias].AnonymizationRules.CommandTimeoutSeconds = 2;
        var factory = new DummyConnectionFactory();
        var logger = new CapturingLogger<AnonymizationRuleProvider>();
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), logger);

        bool first = await provider.IsExcludedAsync(Target("AnyDb"), "dbo", "Customers", "Name", TestContext.Current.CancellationToken);
        bool second = await provider.IsExcludedAsync(Target("AnyDb"), "dbo", "Customers", "Name", TestContext.Current.CancellationToken);
        await accepted.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(first);
        Assert.False(second);
        Assert.Equal(0, factory.ConnectionCreatedCount);
        Assert.NotEmpty(logger.Entries);
        foreach (var (message, exception) in logger.Entries)
        {
            Assert.Contains("AnonymizationRules", message);
            Assert.Contains("connection", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rules-password-marker", message);
            Assert.Null(exception);
        }
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
        var factory = new DummyConnectionFactory(MockConnection.Create(rows, new MockConnectionFlags(HasSchemaPatternColumn: true)));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool dboExcluded = await provider.IsExcludedAsync(Target("AnyDb"), "dbo", "Kunden", "Email", TestContext.Current.CancellationToken);
        bool archivExcluded = await provider.IsExcludedAsync(Target("AnyDb"), "Archiv", "Kunden", "Email", TestContext.Current.CancellationToken);

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
        var factory = new DummyConnectionFactory(MockConnection.Create(rows, new MockConnectionFlags(HasSchemaPatternColumn: false)));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool dboExcluded = await provider.IsExcludedAsync(Target("AnyDb"), "dbo", "Kunden", "Email", TestContext.Current.CancellationToken);
        bool archivExcluded = await provider.IsExcludedAsync(Target("AnyDb"), "Archiv", "Kunden", "Email", TestContext.Current.CancellationToken);

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
        var factory = new DummyConnectionFactory(MockConnection.Create(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excluded = await provider.IsExcludedAsync(Target("StagingDB"), "dbo", "Mitarbeiter", "SSN", TestContext.Current.CancellationToken);

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
        var factory = new DummyConnectionFactory(MockConnection.Create(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool nameExcluded = await provider.IsExcludedAsync(Target("AnyDb"), string.Empty, "FakeConsultants", "FullName", TestContext.Current.CancellationToken);

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
        var factory = new DummyConnectionFactory(MockConnection.Create(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excludedFoo = await provider.IsExcludedAsync(Target("SpecificDb"), "dbo", "AnyTable", "Foo", TestContext.Current.CancellationToken);

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
        var factory = new DummyConnectionFactory(MockConnection.Create(rows));
        var provider = new AnonymizationRuleProvider(factory, Options.Create(options), NullLogger<AnonymizationRuleProvider>.Instance);

        bool excludedFoo = await provider.IsExcludedAsync(Target("SpecificDb"), "dbo", "AnyTable", "Foo", TestContext.Current.CancellationToken);

        Assert.True(excludedFoo); // both candidates are permissive, so either pick allows raw output
    }
}
