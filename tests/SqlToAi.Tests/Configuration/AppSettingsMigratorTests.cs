#nullable enable

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SqlToAi.Configuration;
using Xunit;

namespace SqlToAi.Tests.Configuration;

// @covers SqlToAi.Configuration.AppSettingsMigrator
// @covers SqlToAi.Configuration.MigrationResult
public sealed class AppSettingsMigratorTests : IDisposable
{
    private readonly string _tempDirectory;

    public AppSettingsMigratorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SqlToAi_MigratorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    [Fact]
    public void GetEmbeddedDefaultStream_ShouldReturnNonNullStream()
    {
        // Act
        using Stream? stream = AppSettingsMigrator.GetEmbeddedDefaultStream();

        // Assert
        Assert.NotNull(stream);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void Migrate_ShouldCreateFile_WhenTargetFileDoesNotExist()
    {
        // Arrange
        string targetFilePath = Path.Combine(_tempDirectory, "appsettings.json");
        string defaultJson = """
        {
          "SqlToAi": {
            "SqlServer": { "Server": "default-server" }
          }
        }
        """;
        using var defaultStream = new MemoryStream(Encoding.UTF8.GetBytes(defaultJson));

        // Act
        MigrationResult result = AppSettingsMigrator.Migrate(targetFilePath, defaultStream);

        // Assert
        Assert.True(result.MigrationApplied);
        Assert.Null(result.BackupFilePath);
        Assert.True(File.Exists(targetFilePath));
        Assert.Contains(result.LogEntries, l => l.Contains("Created initial configuration"));

        string createdText = File.ReadAllText(targetFilePath);
        Assert.Contains("default-server", createdText);
    }

    [Fact]
    public void Migrate_ShouldAddNewKeysAndRemoveObsoleteKeys_AndPreserveUserValues()
    {
        // Arrange
        string targetFilePath = Path.Combine(_tempDirectory, "appsettings.json");
        string userJsonText = """
        {
          "SqlToAi": {
            "SqlServer": {
              "Server": "my-custom-prod-server",
              "ObsoleteSetting": "should-be-removed"
            },
            "OldSection": {
              "Key": "obsolete-section"
            }
          }
        }
        """;
        File.WriteAllText(targetFilePath, userJsonText, Encoding.UTF8);

        string defaultJsonText = """
        {
          "SqlToAi": {
            "SqlServer": {
              "Server": "default-server",
              "NewSetting": "factory-default-value"
            },
            "NewSection": {
              "Enabled": true
            }
          }
        }
        """;
        using var defaultStream = new MemoryStream(Encoding.UTF8.GetBytes(defaultJsonText));

        // Act
        MigrationResult result = AppSettingsMigrator.Migrate(targetFilePath, defaultStream);

        // Assert
        Assert.True(result.MigrationApplied);
        Assert.NotNull(result.BackupFilePath);
        Assert.True(File.Exists(result.BackupFilePath));

        string updatedText = File.ReadAllText(targetFilePath);
        JsonNode? updatedNode = JsonNode.Parse(updatedText);

        Assert.NotNull(updatedNode);
        JsonObject sqlToAi = updatedNode["SqlToAi"]!.AsObject();

        // 1. Preserved user value
        Assert.Equal("my-custom-prod-server", sqlToAi["SqlServer"]!["Server"]!.GetValue<string>());

        // 2. Added new keys with defaults
        Assert.Equal("factory-default-value", sqlToAi["SqlServer"]!["NewSetting"]!.GetValue<string>());
        Assert.True(sqlToAi["NewSection"]!["Enabled"]!.GetValue<bool>());

        // 3. Removed obsolete keys
        Assert.Null(sqlToAi["SqlServer"]!["ObsoleteSetting"]);
        Assert.Null(sqlToAi["OldSection"]);

        // 4. Log checks
        Assert.Contains(result.LogEntries, l => l.Contains("Added missing configuration key 'SqlToAi:SqlServer:NewSetting'"));
        Assert.Contains(result.LogEntries, l => l.Contains("Removed obsolete configuration key 'SqlToAi:SqlServer:ObsoleteSetting'"));
        Assert.Contains(result.LogEntries, l => l.Contains("Removed obsolete configuration key 'SqlToAi:OldSection'"));
    }

    [Fact]
    public void Migrate_ShouldNotModifyFile_WhenSchemaMatches()
    {
        // Arrange
        string targetFilePath = Path.Combine(_tempDirectory, "appsettings.json");
        string jsonText = """
        {
          "SqlToAi": {
            "SqlServer": {
              "Server": "my-server"
            }
          }
        }
        """;
        File.WriteAllText(targetFilePath, jsonText, Encoding.UTF8);

        string defaultJsonText = """
        {
          "SqlToAi": {
            "SqlServer": {
              "Server": "default-server"
            }
          }
        }
        """;
        using var defaultStream = new MemoryStream(Encoding.UTF8.GetBytes(defaultJsonText));

        // Act
        MigrationResult result = AppSettingsMigrator.Migrate(targetFilePath, defaultStream);

        // Assert
        Assert.False(result.MigrationApplied);
        Assert.Null(result.BackupFilePath);
        Assert.False(File.Exists(targetFilePath + ".bak"));
    }
}
