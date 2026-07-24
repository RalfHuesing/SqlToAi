#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;

namespace SqlToAi.Tests.Anonymization;

// @covers SqlToAi.Anonymization.AnonymizerExclusionProvider
// @covers SqlToAi.Anonymization.ExclusionCheckResult
// @covers SqlToAi.Anonymization.AnonymizerExclusionSet
public sealed class AnonymizerExclusionProviderTests
{
    private static readonly Type TargetType = typeof(AnonymizerExclusionProvider);
    private static readonly Type ExclusionSetTargetType = typeof(AnonymizerExclusionSet);

    [Fact]
    public async Task GetExclusionsAsync_ShouldReturnEmpty_WhenExclusionSqlIsEmpty()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.AnonymizerExclusionSql = "";

        var mockFactory = new ExclusionDummyConnectionFactory();
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exclusions.Count);
        Assert.Equal(0, mockFactory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task GetExclusionsAsync_ShouldLoadExclusions_AndCacheThemWithTtl()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.AnonymizerExclusionSql = "SELECT TableName, ColumnName FROM Exclusions";
        options.Databases.CacheTtlSeconds = 1; // 1 second TTL

        var initialRows = new List<ExclusionRow>
        {
            new("Kunden", "Name"),
            new("FakeProjects", "ProjectName")
        };

        var mockConn = new ExclusionMockConnection(initialRows);
        var mockFactory = new ExclusionDummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act & Assert
        // First Call: Queries DB
        var exclusions1 = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);
        Assert.Equal(2, exclusions1.Count);
        Assert.True(exclusions1.Contains(null, "Kunden", "Name"));
        Assert.True(exclusions1.Contains(null, "FakeProjects", "ProjectName"));
        Assert.Equal(1, mockFactory.ConnectionCreatedCount);

        // Second Call (Immediate): Cached
        var exclusions2 = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);
        Assert.Equal(2, exclusions2.Count);
        Assert.Equal(1, mockFactory.ConnectionCreatedCount); // No new connection

        // Wait for TTL expiration
        await Task.Delay(1100, TestContext.Current.CancellationToken);

        // Third Call (After TTL): DB queried again
        var exclusions3 = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);
        Assert.Equal(2, exclusions3.Count);
        Assert.Equal(2, mockFactory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task GetExclusionsAsync_ShouldReturnEmpty_WhenSqlThrowsException()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.AnonymizerExclusionSql = "SELECT TableName, ColumnName FROM Exclusions";

        var mockConn = new ExclusionMockConnection(new List<ExclusionRow>(), new ExclusionMockFlags(ThrowException: true));
        var mockFactory = new ExclusionDummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exclusions.Count);
    }

    [Fact]
    public async Task GetExclusionsAsync_ShouldLoadFromExclusionTable_WhenTableExists()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.ExclusionTableName = "dbo.MyExclusions";

        var tableRows = new List<ExclusionRow>
        {
            new("Kunden", "Vorname"),
            new("Bestellungen", "BestellNr")
        };

        var mockConn = new ExclusionMockConnection(tableRows, simulatedTableName: "dbo.MyExclusions");
        var mockFactory = new ExclusionDummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, exclusions.Count);
        Assert.True(exclusions.Contains(null, "Kunden", "Vorname"));
        Assert.True(exclusions.Contains(null, "Bestellungen", "BestellNr"));
    }

    [Fact]
    public async Task GetExclusionsAsync_ShouldNotLoadFromExclusionTable_WhenTableDoesNotExist()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.ExclusionTableName = "dbo.MyExclusions";

        // Simulated table name = null means table does not exist
        var mockConn = new ExclusionMockConnection([], simulatedTableName: null);
        var mockFactory = new ExclusionDummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exclusions.Count);
    }

    [Fact]
    public async Task GetExclusionsAsync_ShouldFallBackSafely_WhenExclusionTableQueryFails()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.ExclusionTableName = "dbo.MyExclusions";

        // Throw exception simulates a database error during query
        var mockConn = new ExclusionMockConnection([], new ExclusionMockFlags(ThrowException: true));
        var mockFactory = new ExclusionDummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exclusions.Count);
    }

    // -------------------------------------------------------------------------
    // Schema-scoped exclusions (audit finding — see
    // tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md, Finding "Ausschluss-/Regel-Abgleich
    // ist schema-blind — gleichnamige Tabelle in anderem Schema erbt fremde Freigabe"). Reproduces
    // the exact scenario: dbo.Kunden and Archiv.Kunden both have an Email column.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetExclusionsAsync_ShouldIsolateSchemas_WhenCustomSqlReturnsThirdSchemaColumn()
    {
        // Arrange: AnonymizerExclusionSql returns a 3rd column, treated positionally as the schema.
        var options = new SqlToAiOptions();
        options.Databases.AnonymizerExclusionSql = "SELECT TableName, ColumnName, SchemaName FROM Exclusions";

        var rows = new List<ExclusionRow> { new("Kunden", "Email", "dbo") };
        var mockConn = new ExclusionMockConnection(rows, fieldCount: 3);
        var mockFactory = new ExclusionDummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert: only the schema this entry names is exempted; the same-named table in another
        // schema must still be anonymized.
        Assert.True(exclusions.Contains("dbo", "Kunden", "Email"));
        Assert.False(exclusions.Contains("Archiv", "Kunden", "Email"));
    }

    [Fact]
    public async Task GetExclusionsAsync_ShouldApplyToEverySchema_WhenCustomSqlReturnsOnlyTwoColumns()
    {
        // Backward-compatibility regression: no schema qualifier at all (the historical shape)
        // must keep applying across every schema, exactly as before schema scoping existed.
        var options = new SqlToAiOptions();
        options.Databases.AnonymizerExclusionSql = "SELECT TableName, ColumnName FROM Exclusions";

        var rows = new List<ExclusionRow> { new("Kunden", "Email") };
        var mockConn = new ExclusionMockConnection(rows);
        var mockFactory = new ExclusionDummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(exclusions.Contains("dbo", "Kunden", "Email"));
        Assert.True(exclusions.Contains("Archiv", "Kunden", "Email"));
    }

    [Fact]
    public async Task GetExclusionsAsync_ShouldIsolateSchemas_WhenExclusionTableHasSchemaNameColumn()
    {
        // Arrange: the physical exclusion table already has the optional SchemaName column.
        var options = new SqlToAiOptions();
        options.Anonymizer.ExclusionTableName = "dbo.MyExclusions";

        var rows = new List<ExclusionRow> { new("Kunden", "Email", "dbo") };
        var mockConn = new ExclusionMockConnection(rows, new ExclusionMockFlags(HasSchemaColumn: true), simulatedTableName: "dbo.MyExclusions");
        var mockFactory = new ExclusionDummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(exclusions.Contains("dbo", "Kunden", "Email"));
        Assert.False(exclusions.Contains("Archiv", "Kunden", "Email"));
    }

    [Fact]
    public async Task GetExclusionsAsync_ShouldDegradeGracefully_WhenExclusionTableHasNoSchemaNameColumn()
    {
        // Backward-compatibility regression: a customer database that hasn't run the migration
        // adding [SchemaName] to the physical table must keep working with zero-config,
        // schema-agnostic matching — no crash, no missing exclusions.
        var options = new SqlToAiOptions();
        options.Anonymizer.ExclusionTableName = "dbo.MyExclusions";

        var rows = new List<ExclusionRow> { new("Kunden", "Email") };
        var mockConn = new ExclusionMockConnection(rows, new ExclusionMockFlags(HasSchemaColumn: false), simulatedTableName: "dbo.MyExclusions");
        var mockFactory = new ExclusionDummyConnectionFactory(mockConn);
        var provider = new AnonymizerExclusionProvider(mockFactory, Options.Create(options), NullLogger<AnonymizerExclusionProvider>.Instance);

        // Act
        var exclusions = await provider.GetExclusionsAsync("TestDb", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(exclusions.Contains("dbo", "Kunden", "Email"));
        Assert.True(exclusions.Contains("Archiv", "Kunden", "Email"));
    }
}
