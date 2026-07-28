#nullable enable

using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;
using Xunit;

namespace SqlToAi.Tests.Security;

// @covers SqlToAi.Security.AccessLevelProvider
public sealed class AccessLevelProviderTests
{
    [Fact]
    public async Task GetAccessLevelAsync_ShouldReturnNone_WhenDatabaseIsWhitespaceOrNull()
    {
        // Arrange
        var options = new SqlToAiOptions();
        var provider = new AccessLevelProvider(Options.Create(options));

        // Act
        var levelNull = await provider.GetAccessLevelAsync("", TestContext.Current.CancellationToken);
        var levelWs = await provider.GetAccessLevelAsync("   ", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AccessLevel.None, levelNull);
        Assert.Equal(AccessLevel.None, levelWs);
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldReturnNone_WhenDatabaseIsNotInAnyList()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = ["AllowedDB"];
        var provider = new AccessLevelProvider(Options.Create(options));

        // Act
        var level = await provider.GetAccessLevelAsync("UnknownDB", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AccessLevel.None, level);
    }

    [Theory]
    [InlineData("ReadWrite", "DemoDB", AccessLevel.ReadWrite)]
    [InlineData("ReadOnly", "ArchiveDB", AccessLevel.ReadOnly)]
    [InlineData("ReadOnlyAnonymized", "CustomerDB", AccessLevel.ReadOnlyAnonymized)]
    [InlineData("SchemaOnly", "SystemDB", AccessLevel.SchemaOnly)]
    public async Task GetAccessLevelAsync_ShouldReturnCorrectLevel_WhenConfiguredInSpecificList(
        string listName, string dbName, AccessLevel expectedLevel)
    {
        // Arrange
        var options = new SqlToAiOptions();
        switch (listName)
        {
            case "ReadWrite":
                options.Databases.ReadWrite = [dbName];
                break;
            case "ReadOnly":
                options.Databases.ReadOnly = [dbName];
                break;
            case "ReadOnlyAnonymized":
                options.Databases.ReadOnlyAnonymized = [dbName];
                break;
            case "SchemaOnly":
                options.Databases.SchemaOnly = [dbName];
                break;
        }

        var provider = new AccessLevelProvider(Options.Create(options));

        // Act
        var level = await provider.GetAccessLevelAsync(dbName.ToLowerInvariant(), TestContext.Current.CancellationToken);

        // Assert — Case-insensitive exact match
        Assert.Equal(expectedLevel, level);
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldResolveConflictsUsingMostRestrictiveLevel()
    {
        // Arrange — DB is present in multiple lists. Restrictive order: SchemaOnly > ReadOnlyAnonymized > ReadOnly > ReadWrite
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = ["ConflictedDB"];
        options.Databases.ReadOnly = ["ConflictedDB"];
        options.Databases.ReadOnlyAnonymized = ["ConflictedDB"];
        options.Databases.SchemaOnly = ["ConflictedDB"];

        var provider = new AccessLevelProvider(Options.Create(options));

        // Act
        var level = await provider.GetAccessLevelAsync("ConflictedDB", TestContext.Current.CancellationToken);

        // Assert — Most restrictive (SchemaOnly) wins
        Assert.Equal(AccessLevel.SchemaOnly, level);
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldPreferReadOnlyOverReadWrite_WhenInBothLists()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.ReadWrite = ["SharedDB"];
        options.Databases.ReadOnly = ["SharedDB"];

        var provider = new AccessLevelProvider(Options.Create(options));

        // Act
        var level = await provider.GetAccessLevelAsync("SharedDB", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AccessLevel.ReadOnly, level);
    }
}
