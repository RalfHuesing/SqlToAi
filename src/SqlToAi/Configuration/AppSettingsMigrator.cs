#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SqlToAi.Configuration;

/// <summary>
/// Result of an appsettings.json migration run.
/// </summary>
public sealed record MigrationResult(
    bool MigrationApplied,
    string? BackupFilePath,
    IReadOnlyList<string> LogEntries);

/// <summary>
/// Smart auto-migrator that synchronizes local appsettings.json with embedded factory defaults.
/// </summary>
public sealed class AppSettingsMigrator
{
    private static readonly string[] MissingEmbeddedDefaultLog = new[]
    {
        "Embedded factory default appsettings.json not found in assembly resources."
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Synchronizes the target configuration file at <paramref name="targetFilePath"/> with the embedded factory default.
    /// </summary>
    public static MigrationResult Migrate(string targetFilePath)
    {
        using Stream? defaultStream = GetEmbeddedDefaultStream();
        if (defaultStream == null)
        {
            return new MigrationResult(false, null, MissingEmbeddedDefaultLog);
        }

        return Migrate(targetFilePath, defaultStream);
    }

    /// <summary>
    /// Synchronizes the target configuration file at <paramref name="targetFilePath"/> with the default JSON from <paramref name="defaultJsonStream"/>.
    /// </summary>
    public static MigrationResult Migrate(string targetFilePath, Stream defaultJsonStream)
    {
        var logs = new List<string>();

        if (!File.Exists(targetFilePath))
        {
            return CreateInitialConfiguration(targetFilePath, defaultJsonStream, logs);
        }

        return SyncExistingConfiguration(targetFilePath, defaultJsonStream, logs);
    }

    /// <summary>
    /// Retrieves the embedded default appsettings.json stream from the assembly manifest resources.
    /// </summary>
    public static Stream? GetEmbeddedDefaultStream()
    {
        Assembly assembly = typeof(AppSettingsMigrator).Assembly;
        return assembly.GetManifestResourceStream("SqlToAi.appsettings.json");
    }

    private static MigrationResult CreateInitialConfiguration(string targetFilePath, Stream defaultJsonStream, List<string> logs)
    {
        string? directoryPath = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        using var reader = new StreamReader(defaultJsonStream, Encoding.UTF8);
        string defaultContent = reader.ReadToEnd();

        File.WriteAllText(targetFilePath, defaultContent, new UTF8Encoding(false));
        logs.Add($"Created initial configuration '{targetFilePath}' from factory default template.");

        return new MigrationResult(true, null, logs);
    }

    private static MigrationResult SyncExistingConfiguration(string targetFilePath, Stream defaultJsonStream, List<string> logs)
    {
        string targetJsonText = File.ReadAllText(targetFilePath, Encoding.UTF8);

        JsonNode? targetNode = ParseJsonNode(targetJsonText);
        JsonNode? defaultNode = JsonNode.Parse(defaultJsonStream);

        if (targetNode is not JsonObject targetObj || defaultNode is not JsonObject defaultObj)
        {
            logs.Add("Configuration JSON root is not a valid JSON object. Skipping auto-migration.");
            return new MigrationResult(false, null, logs);
        }

        bool changesMade = SyncJsonObject(targetObj, defaultObj, string.Empty, logs);
        if (!changesMade)
        {
            return new MigrationResult(false, null, logs);
        }

        string backupPath = CreateBackupFile(targetFilePath, logs);
        SaveUpdatedJson(targetFilePath, targetObj);

        logs.Add($"Updated '{targetFilePath}' with new factory default settings and removed obsolete keys.");
        return new MigrationResult(true, backupPath, logs);
    }

    private static JsonNode? ParseJsonNode(string jsonText)
    {
        try
        {
            return JsonNode.Parse(jsonText);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool SyncJsonObject(JsonObject targetObj, JsonObject defaultObj, string pathPrefix, List<string> logs)
    {
        bool keysAddedOrUpdated = AddMissingKeysAndRecurse(targetObj, defaultObj, pathPrefix, logs);
        bool keysRemoved = RemoveObsoleteKeys(targetObj, defaultObj, pathPrefix, logs);
        return keysAddedOrUpdated || keysRemoved;
    }

    private static bool AddMissingKeysAndRecurse(JsonObject targetObj, JsonObject defaultObj, string pathPrefix, List<string> logs)
    {
        bool changes = false;
        var defaultKeys = new List<KeyValuePair<string, JsonNode?>>(defaultObj);
        foreach (KeyValuePair<string, JsonNode?> defaultItem in defaultKeys)
        {
            string key = defaultItem.Key;
            string currentPath = string.IsNullOrEmpty(pathPrefix) ? key : $"{pathPrefix}:{key}";
            JsonNode? defaultVal = defaultItem.Value;

            if (SyncSingleKey(targetObj, defaultVal, key, currentPath, logs))
            {
                changes = true;
            }
        }
        return changes;
    }

    private static bool SyncSingleKey(JsonObject targetObj, JsonNode? defaultVal, string key, string currentPath, List<string> logs)
    {
        if (!targetObj.ContainsKey(key))
        {
            targetObj.Add(key, defaultVal?.DeepClone());
            logs.Add($"Added missing configuration key '{currentPath}' with factory default value.");
            return true;
        }

        if (targetObj[key] is JsonObject targetSubObj && defaultVal is JsonObject defaultSubObj)
        {
            return SyncJsonObject(targetSubObj, defaultSubObj, currentPath, logs);
        }

        return false;
    }

    private static bool RemoveObsoleteKeys(JsonObject targetObj, JsonObject defaultObj, string pathPrefix, List<string> logs)
    {
        bool changes = false;
        var targetKeys = new List<string>(targetObj.Select(kv => kv.Key));
        foreach (string key in targetKeys)
        {
            if (defaultObj.ContainsKey(key))
            {
                continue;
            }

            targetObj.Remove(key);
            string currentPath = string.IsNullOrEmpty(pathPrefix) ? key : $"{pathPrefix}:{key}";
            logs.Add($"Removed obsolete configuration key '{currentPath}'.");
            changes = true;
        }
        return changes;
    }

    private const string MaskedPasswordPlaceholder = "***MASKED-BY-MIGRATOR***";

    internal static string CreateBackupFile(string targetFilePath, List<string> logs)
    {
        string backupPath = targetFilePath + ".bak";
        try
        {
            string originalJson = File.ReadAllText(targetFilePath);
            using JsonDocument document = JsonDocument.Parse(originalJson);
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions
            {
                Indented = true,
                Encoder = JsonOptions.Encoder
            }))
            {
                if (WriteMaskedElement(writer, document.RootElement))
                {
                    writer.Flush();
                    string maskedJson = Encoding.UTF8.GetString(stream.ToArray());
                    File.WriteAllText(backupPath, maskedJson, new UTF8Encoding(false));
                    logs.Add($"Saved backup configuration to '{backupPath}' (Password field masked).");
                    return backupPath;
                }
            }
        }
        catch (Exception ex)
        {
            logs.Add($"Warning: Could not mask Password field in backup file '{backupPath}': {ex.Message}. Falling back to 1:1 copy.");
        }

        File.Copy(targetFilePath, backupPath, overwrite: true);
        logs.Add($"Saved backup configuration to '{backupPath}'.");
        return backupPath;
    }

    private static bool WriteMaskedElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return WriteMaskedObject(writer, element);
            case JsonValueKind.Array:
                return WriteMaskedArray(writer, element);
            default:
                element.WriteTo(writer);
                return false;
        }
    }

    private static bool WriteMaskedObject(Utf8JsonWriter writer, JsonElement element)
    {
        bool masked = false;
        writer.WriteStartObject();
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, "Password", StringComparison.OrdinalIgnoreCase)
                && ShouldMaskPasswordValue(property.Value))
            {
                writer.WriteString(property.Name, MaskedPasswordPlaceholder);
                masked = true;
            }
            else
            {
                writer.WritePropertyName(property.Name);
                if (WriteMaskedElement(writer, property.Value))
                {
                    masked = true;
                }
            }
        }
        writer.WriteEndObject();
        return masked;
    }

    private static bool WriteMaskedArray(Utf8JsonWriter writer, JsonElement element)
    {
        bool masked = false;
        writer.WriteStartArray();
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (WriteMaskedElement(writer, item))
            {
                masked = true;
            }
        }
        writer.WriteEndArray();
        return masked;
    }

    private static bool ShouldMaskPasswordValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return false;
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            if (text is null)
            {
                return true;
            }
            return !IsEnvironmentVariableReference(text);
        }
        return true;
    }

    private static bool IsEnvironmentVariableReference(string value)
    {
        return value.StartsWith('%') && value.EndsWith('%');
    }

    private static void SaveUpdatedJson(string targetFilePath, JsonObject targetObj)
    {
        string updatedJson = targetObj.ToJsonString(JsonOptions);
        File.WriteAllText(targetFilePath, updatedJson, new UTF8Encoding(false));
    }
}
