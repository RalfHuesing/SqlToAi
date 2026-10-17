using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Metadata;
using SqlToAi.Security;

namespace SqlToAi.Tests.Database;

#pragma warning disable CS8765

/// <summary>Covers <see cref="SchemaService"/>'s "Anonymized" column annotation specifically — split out from <see cref="SchemaServiceTests"/> to keep both files under the line-count limit.</summary>
public sealed class SchemaServiceAnonymizationTests
{
    [Fact]
    public async Task GetSchemaAsync_PropagatesRequestedCancellationFromCentralRules()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var options = new SqlToAiOptions();
        options.Databases.SchemaOnly = [TestConstants.DatabaseName];
        options.MetadataProvider.Enabled = false;
        options.Anonymizer.Enabled = true;
        options.AnonymizationRules.Enabled = true;
        options.AnonymizationRules.TableName = "dbo.AnonymizationRules";
        var bound = Options.Create(options);
        var ruleConnection = Anonymization.MockConnection.Create([], beforeExecute: command =>
        {
            if (command.CommandText.Contains("COL_LENGTH", StringComparison.Ordinal))
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
        });
        var ruleFactory = new Anonymization.DummyConnectionFactory(ruleConnection);
        var rules = new AnonymizationRuleProvider(ruleFactory, bound, NullLogger<AnonymizationRuleProvider>.Instance);
        var schemaFactory = new DummyConnectionFactory();
        var service = new SchemaService(schemaFactory, new SecurityGuard(bound),
            new AccessLevelProvider(schemaFactory, bound, NullLogger<AccessLevelProvider>.Instance),
            new MetadataProvider(schemaFactory, bound, NullLogger<MetadataProvider>.Instance),
            new AnonymizationPolicyResolver(bound, rules), bound, NullLogger<SchemaService>.Instance);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetSchemaAsync(TestConstants.DatabaseName, "dbo.Customers", cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, ruleFactory.ConnectionCreatedCount);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldAnnotateAnonymizedColumn_UsingPolicyResolver()
    {
        // Arrange — mock schema returns CustomerId (int, PK/Identity) and Email (varchar, nullable).
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];

        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);
        var policyResolver = new SelectiveAnonymizePolicyResolver("Email");

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, policyResolver, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaAsync(TestConstants.DatabaseName, "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("Anonymized", result.Value);
        // Email is a string column the resolver flags -> "Yes".
        Assert.Contains("| Email | varchar(100) | Yes |  | Yes |  |", result.Value);
        // CustomerId is an int column -> never anonymized regardless of the resolver ("No"),
        // because Anonymizer only ever touches string values.
        Assert.Contains("| CustomerId | int | No | PK, Identity | No |  |", result.Value);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldAnnotateSearchableColumn_AsYesSearchable()
    {
        // Arrange — same mock schema (CustomerId int, Email varchar), but the resolver now flags
        // Email as both anonymized AND searchable-token (reversible), not just masked.
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = [TestConstants.DatabaseName];

        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);
        var policyResolver = new SearchableAnonymizePolicyResolver("Email");

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, policyResolver, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaAsync(TestConstants.DatabaseName, "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("| Email | varchar(100) | Yes |  | Yes (searchable) |  |", result.Value);
        Assert.Contains("| CustomerId | int | No | PK, Identity | No |  |", result.Value);
    }
}
