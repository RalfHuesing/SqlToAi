#nullable enable

using System;
using System.Collections.Generic;
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
        Assert.Matches(@"appsettings\.json\.\d{8}_\d{6}\.bak$", result.BackupFilePath);
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
        Assert.Empty(Directory.GetFiles(_tempDirectory, "*.bak"));
    }

    [Fact]
    public void Migrate_ShouldNotEscapeSingleQuotesInJsonOutput()
    {
        // Arrange
        string targetFilePath = Path.Combine(_tempDirectory, "appsettings.json");
        string userJsonText = """
        {
          "SqlToAi": {
            "Databases": {
              "AccessCheckSql": "SELECT CASE WHEN DB_NAME() IN ('OLDemoReweAbfD') THEN 'ReadWrite' ELSE 'ReadOnlyAnonymized' END AS AccessLevel"
            }
          }
        }
        """;
        File.WriteAllText(targetFilePath, userJsonText, Encoding.UTF8);

        string defaultJsonText = """
        {
          "SqlToAi": {
            "Databases": {
              "AccessCheckSql": "SELECT 1",
              "NewKey": "NewValue"
            }
          }
        }
        """;
        using var defaultStream = new MemoryStream(Encoding.UTF8.GetBytes(defaultJsonText));

        // Act
        MigrationResult result = AppSettingsMigrator.Migrate(targetFilePath, defaultStream);

        // Assert
        Assert.True(result.MigrationApplied);
        string updatedText = File.ReadAllText(targetFilePath);
        Assert.Contains("'OLDemoReweAbfD'", updatedText);
        Assert.DoesNotContain("\\u0027", updatedText);
    }

    [Fact]
    public void CreateBackupFile_ShouldMaskPassword_WhenPlaintextPresent()
    {
        // Arrange
        string targetFilePath = Path.Combine(_tempDirectory, "appsettings.json");
        string userJsonText = """
        {
          "SqlToAi": {
            "SqlServer": {
              "Server": "my-server",
              "UserId": "Agent",
              "Password": "Agent!"
            }
          }
        }
        """;
        File.WriteAllText(targetFilePath, userJsonText, Encoding.UTF8);
        var logs = new List<string>();

        // Act
        string backupPath = AppSettingsMigrator.CreateBackupFile(targetFilePath, logs);

        // Assert
        Assert.True(File.Exists(backupPath));
        string backupText = File.ReadAllText(backupPath);
        JsonNode? root = JsonNode.Parse(backupText);
        Assert.NotNull(root);

        // Password field is replaced with the static placeholder
        Assert.Equal("***MASKED-BY-MIGRATOR***", root!["SqlToAi"]!["SqlServer"]!["Password"]!.GetValue<string>());

        // Original plaintext is no longer present anywhere in the backup
        Assert.DoesNotContain("Agent!", backupText);

        // Log entry signals that masking happened
        Assert.Contains(logs, l => l.Contains("Password field masked"));
    }

    [Fact]
    public void CreateBackupFile_ShouldNotMaskPassword_WhenEnvironmentVariableReferenced()
    {
        // Arrange
        string targetFilePath = Path.Combine(_tempDirectory, "appsettings.json");
        string userJsonText = """
        {
          "SqlToAi": {
            "SqlServer": {
              "Server": "my-server",
              "Password": "%SQLTOAI_PASSWORD%"
            }
          }
        }
        """;
        File.WriteAllText(targetFilePath, userJsonText, Encoding.UTF8);
        var logs = new List<string>();

        // Act
        string backupPath = AppSettingsMigrator.CreateBackupFile(targetFilePath, logs);

        // Assert
        Assert.True(File.Exists(backupPath));
        string backupText = File.ReadAllText(backupPath);
        JsonNode? root = JsonNode.Parse(backupText);
        Assert.NotNull(root);

        // Env-var reference is preserved verbatim
        Assert.Equal("%SQLTOAI_PASSWORD%", root!["SqlToAi"]!["SqlServer"]!["Password"]!.GetValue<string>());

        // Backup is byte-identical to the original (1:1 copy path)
        Assert.Equal(userJsonText, backupText);
    }

    [Fact]
    public void CreateBackupFile_ShouldLeaveOtherFieldsUnchanged()
    {
        // Arrange
        string targetFilePath = Path.Combine(_tempDirectory, "appsettings.json");
        string userJsonText = """
        {
          "SqlToAi": {
            "SqlServer": {
              "Server": "my-server",
              "UserId": "Agent",
              "Password": "PlainPassword123",
              "CacheTtlSeconds": 300
            }
          }
        }
        """;
        File.WriteAllText(targetFilePath, userJsonText, Encoding.UTF8);
        var logs = new List<string>();

        // Act
        string backupPath = AppSettingsMigrator.CreateBackupFile(targetFilePath, logs);

        // Assert
        string backupText = File.ReadAllText(backupPath);
        JsonNode? original = JsonNode.Parse(userJsonText);
        JsonNode? backup = JsonNode.Parse(backupText);
        Assert.NotNull(original);
        Assert.NotNull(backup);

        JsonObject origSqlServer = original!["SqlToAi"]!["SqlServer"]!.AsObject();
        JsonObject backupSqlServer = backup!["SqlToAi"]!["SqlServer"]!.AsObject();

        // Non-Password fields are byte-identical to the source
        Assert.Equal(origSqlServer["Server"]!.GetValue<string>(), backupSqlServer["Server"]!.GetValue<string>());
        Assert.Equal(origSqlServer["UserId"]!.GetValue<string>(), backupSqlServer["UserId"]!.GetValue<string>());
        Assert.Equal(origSqlServer["CacheTtlSeconds"]!.GetValue<int>(), backupSqlServer["CacheTtlSeconds"]!.GetValue<int>());

        // Only the Password field changed
        Assert.Equal("***MASKED-BY-MIGRATOR***", backupSqlServer["Password"]!.GetValue<string>());
        Assert.NotEqual(origSqlServer["Password"]!.GetValue<string>(), backupSqlServer["Password"]!.GetValue<string>());
    }

    [Fact]
    public void CreateBackupFile_ShouldUseTimestampInFilename()
    {
        // Arrange
        string targetFilePath = Path.Combine(_tempDirectory, "appsettings.json");
        File.WriteAllText(targetFilePath, "{}", Encoding.UTF8);
        var logs = new List<string>();

        // Act
        string backupPath = AppSettingsMigrator.CreateBackupFile(targetFilePath, logs);

        // Assert
        Assert.Matches(@"appsettings\.json\.\d{8}_\d{6}\.bak$", backupPath);
        Assert.True(File.Exists(backupPath));
    }
}
