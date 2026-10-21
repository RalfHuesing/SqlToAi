using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;
using Xunit;

namespace SqlToAi.Tests.Security;

public sealed class AccessLevelProviderTests
{
    private static readonly DatabaseTargetContext Target = new("primary", "DemoDB");

    [Fact]
    public async Task GetTargetAccessLevelAsync_ShouldReturnNone_WhenServerNotConfigured()
    {
        var options = new SqlToAiOptions();
        var provider = new AccessLevelProvider(Options.Create(options));

        var level = await provider.GetTargetAccessLevelAsync(Target, TestContext.Current.CancellationToken);

        Assert.Equal(AccessLevel.None, level);
    }

    [Fact]
    public async Task GetTargetAccessLevelAsync_ShouldReturnNone_WhenTargetIsNotInAnyList()
    {
        var options = new SqlToAiOptions().WithNamedServers("primary");
        options.Databases.ReadWrite = ["primary/AllowedDB"];
        var provider = new AccessLevelProvider(Options.Create(options));

        var level = await provider.GetTargetAccessLevelAsync(new DatabaseTargetContext("primary", "UnknownDB"), TestContext.Current.CancellationToken);

        Assert.Equal(AccessLevel.None, level);
    }

    [Theory]
    [InlineData("ReadWrite", "DemoDB", AccessLevel.ReadWrite)]
    [InlineData("ReadWriteAnonymized", "WritableMaskedDB", AccessLevel.ReadWriteAnonymized)]
    [InlineData("ReadOnly", "ArchiveDB", AccessLevel.ReadOnly)]
    [InlineData("ReadOnlyAnonymized", "CustomerDB", AccessLevel.ReadOnlyAnonymized)]
    [InlineData("SchemaOnly", "SystemDB", AccessLevel.SchemaOnly)]
    public async Task GetTargetAccessLevelAsync_ShouldReturnCorrectLevel_WhenConfiguredInSpecificList(
        string listName, string dbName, AccessLevel expectedLevel)
    {
        var options = new SqlToAiOptions().WithNamedServers("primary");
        string qualified = $"primary/{dbName}";
        switch (listName)
        {
            case "ReadWrite":
                options.Databases.ReadWrite = [qualified];
                break;
            case "ReadWriteAnonymized":
                options.Databases.ReadWriteAnonymized = [qualified];
                break;
            case "ReadOnly":
                options.Databases.ReadOnly = [qualified];
                break;
            case "ReadOnlyAnonymized":
                options.Databases.ReadOnlyAnonymized = [qualified];
                break;
            case "SchemaOnly":
                options.Databases.SchemaOnly = [qualified];
                break;
        }

        var provider = new AccessLevelProvider(Options.Create(options));

        var level = await provider.GetTargetAccessLevelAsync(new DatabaseTargetContext("primary", dbName.ToLowerInvariant()), TestContext.Current.CancellationToken);

        Assert.Equal(expectedLevel, level);
    }

    [Fact]
    public async Task GetTargetAccessLevelAsync_ShouldResolveConflictsUsingMostRestrictiveLevel()
    {
        var options = new SqlToAiOptions().WithNamedServers("primary");
        options.Databases.ReadWrite = ["primary/ConflictedDB"];
        options.Databases.ReadWriteAnonymized = ["primary/ConflictedDB"];
        options.Databases.ReadOnly = ["primary/ConflictedDB"];
        options.Databases.ReadOnlyAnonymized = ["primary/ConflictedDB"];
        options.Databases.SchemaOnly = ["primary/ConflictedDB"];

        var provider = new AccessLevelProvider(Options.Create(options));

        var level = await provider.GetTargetAccessLevelAsync(new DatabaseTargetContext("primary", "ConflictedDB"), TestContext.Current.CancellationToken);

        Assert.Equal(AccessLevel.SchemaOnly, level);
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly, AccessLevel.ReadOnlyAnonymized)]
    [InlineData(AccessLevel.ReadOnlyAnonymized, AccessLevel.ReadOnlyAnonymized)]
    [InlineData(AccessLevel.ReadWrite, AccessLevel.ReadWriteAnonymized)]
    [InlineData(AccessLevel.SchemaOnly, AccessLevel.SchemaOnly)]
    public async Task GetTargetAccessLevelAsync_ReadWriteAnonymizedOverlapKeepsMoreRestrictiveGrant(
        AccessLevel overlappingLevel, AccessLevel expectedLevel)
    {
        var options = new SqlToAiOptions().WithNamedServers("primary");
        const string qualified = "primary/SharedDB";
        options.Databases.ReadWriteAnonymized = [qualified];
        switch (overlappingLevel)
        {
            case AccessLevel.ReadOnly:
                options.Databases.ReadOnly = [qualified];
                break;
            case AccessLevel.ReadOnlyAnonymized:
                options.Databases.ReadOnlyAnonymized = [qualified];
                break;
            case AccessLevel.ReadWrite:
                options.Databases.ReadWrite = [qualified];
                break;
            case AccessLevel.SchemaOnly:
                options.Databases.SchemaOnly = [qualified];
                break;
        }

        var provider = new AccessLevelProvider(Options.Create(options));

        var level = await provider.GetTargetAccessLevelAsync(new DatabaseTargetContext("primary", "SharedDB"), TestContext.Current.CancellationToken);

        Assert.Equal(expectedLevel, level);
    }

    [Fact]
    public async Task GetTargetAccessLevelAsync_ShouldPreferReadOnlyOverReadWrite_WhenInBothLists()
    {
        var options = new SqlToAiOptions().WithNamedServers("primary");
        options.Databases.ReadWrite = ["primary/SharedDB"];
        options.Databases.ReadOnly = ["primary/SharedDB"];

        var provider = new AccessLevelProvider(Options.Create(options));

        var level = await provider.GetTargetAccessLevelAsync(new DatabaseTargetContext("primary", "SharedDB"), TestContext.Current.CancellationToken);

        Assert.Equal(AccessLevel.ReadOnly, level);
    }
}
