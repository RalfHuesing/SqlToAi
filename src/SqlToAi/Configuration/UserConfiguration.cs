using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace SqlToAi.Configuration;

internal sealed class UserConfiguration : IDisposable
{
    private UserConfiguration(string rootDirectory, IConfigurationRoot configuration, SqlToAiOptions options)
    {
        RootDirectory = rootDirectory;
        Configuration = configuration;
        Options = options;
    }

    internal string RootDirectory { get; }
    internal IConfigurationRoot Configuration { get; }
    internal SqlToAiOptions Options { get; }

    internal static UserConfiguration? LoadOrReport(string? rootDirectory = null)
    {
        try
        {
            return Load(rootDirectory);
        }
        catch (Exception exception) when (exception is ConfigurationStartupException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Configuration startup failed: {exception.Message}");
            return null;
        }
    }

    internal static string ResolveRoot(string? localApplicationData = null)
    {
        string localRoot = localApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localRoot))
            throw new InvalidOperationException("The Windows Local Application Data directory is unavailable.");
        return Path.GetFullPath(Path.Combine(localRoot, "SqlToAi"));
    }

    // The optional root is an internal seam for isolated tests, never a CLI or environment override.
    internal static UserConfiguration Load(string? rootDirectory = null)
    {
        string root = Path.GetFullPath(rootDirectory ?? ResolveRoot());
        string expectedPath = Path.Combine(root, "appsettings.json");
        IConfigurationRoot? configuration = null;
        try
        {
            CheckObsoleteEnvironmentVariables(expectedPath);

            // Open before constructing providers: even a nonexistent root must fail without creation.
            byte[] baseBytes = File.ReadAllBytes(expectedPath);
            InspectJsonSource(baseBytes, expectedPath, expectedPath);

            string envName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
            string envFile = Path.Combine(root, $"appsettings.{envName}.json");
            if (File.Exists(envFile))
            {
                byte[] envBytes = File.ReadAllBytes(envFile);
                InspectJsonSource(envBytes, envFile, expectedPath);
            }

            using var baseStream = new MemoryStream(baseBytes);
            configuration = new ConfigurationBuilder()
                .SetBasePath(root)
                .AddJsonStream(baseStream)
                .AddJsonFile($"appsettings.{envName}.json",
                    optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();
            var options = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>() ?? new SqlToAiOptions();
            ConfigurationResolver.Resolve(options, root);
            ConfigurationResolver.Validate(options, expectedPath);
            ValidateLoggingOptions(options.Logging, expectedPath);
            ValidateSchemaBatchOptions(options.SchemaBatch, expectedPath);
            EnsureWritableDirectory(root);
            return new UserConfiguration(root, configuration, options);
        }
        catch (Exception exception) when (exception is ConfigurationStartupException or IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or JsonException)
        {
            (configuration as IDisposable)?.Dispose();
            if (exception is ConfigurationStartupException) throw;
            string reason = exception switch
            {
                FileNotFoundException { FileName: { } missingFile } when !string.Equals(missingFile, expectedPath, StringComparison.OrdinalIgnoreCase)
                    => $"A configured SQL file '{missingFile}' is missing",
                FileNotFoundException or DirectoryNotFoundException => "A required settings file is missing",
                UnauthorizedAccessException => "Settings cannot be read because access is denied",
                FormatException or InvalidDataException or JsonException => "Settings JSON is invalid",
                InvalidOperationException => "A settings value is invalid",
                _ => "Settings could not be read"
            };
            throw new ConfigurationStartupException(
                $"{reason} ({exception.GetType().Name}). Expected base settings file: '{expectedPath}'. " +
                "Create and configure this file manually from the supplied appsettings.json template, then restart.", exception);
        }
    }

    private static void CheckObsoleteEnvironmentVariables(string settingsPath)
    {
        var envVars = Environment.GetEnvironmentVariables();
        foreach (object? keyObj in envVars.Keys)
        {
            if (keyObj is string key)
            {
                if (key.StartsWith("SqlToAi__SqlServer", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("SqlToAi__MetadataProvider", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("SqlToAi__AnonymizationRules", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ConfigurationStartupException(
                        $"Obsolete configuration environment variable '{key}' detected. Legacy single-server settings ('SqlServer', 'MetadataProvider', 'AnonymizationRules') have been removed in favor of 'SqlToAi.Servers.<alias>'. " +
                        $"Settings file: '{settingsPath}'. Correct this manually using the supplied appsettings.json template, then restart.");
                }
            }
        }
    }

    private static void InspectJsonSource(byte[] jsonBytes, string filePath, string settingsPath)
    {
        using var document = JsonDocument.Parse(jsonBytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // Check top-level obsolete sections
        foreach (var prop in root.EnumerateObject())
        {
            if (IsObsoleteSection(prop.Name))
            {
                throw new ConfigurationStartupException(
                    $"Obsolete configuration section '{prop.Name}' detected in '{filePath}'. Legacy single-server settings ('SqlServer', 'MetadataProvider', 'AnonymizationRules') have been removed in favor of 'SqlToAi.Servers.<alias>'. " +
                    $"Settings file: '{settingsPath}'. Correct this manually using the supplied appsettings.json template, then restart.");
            }
        }

        // Check SqlToAi-level obsolete sections & server alias uniqueness
        var sqlToAiProp = GetPropertyCaseInsensitive(root, "SqlToAi");
        if (sqlToAiProp is { ValueKind: JsonValueKind.Object } sqlToAiObj)
        {
            foreach (var prop in sqlToAiObj.EnumerateObject())
            {
                if (IsObsoleteSection(prop.Name))
                {
                    throw new ConfigurationStartupException(
                        $"Obsolete configuration section 'SqlToAi.{prop.Name}' detected in '{filePath}'. Legacy single-server settings ('SqlServer', 'MetadataProvider', 'AnonymizationRules') have been removed in favor of 'SqlToAi.Servers.<alias>'. " +
                        $"Settings file: '{settingsPath}'. Correct this manually using the supplied appsettings.json template, then restart.");
                }
            }

            var serversProp = GetPropertyCaseInsensitive(sqlToAiObj, "Servers");
            if (serversProp is { ValueKind: JsonValueKind.Object } serversObj)
            {
                var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var serverProp in serversObj.EnumerateObject())
                {
                    if (!seenAliases.Add(serverProp.Name))
                    {
                        throw new ConfigurationStartupException(
                            $"Duplicate server alias '{serverProp.Name}' detected in '{filePath}'. Server aliases must be unique (case-insensitive). " +
                            $"Settings file: '{settingsPath}'. Correct this manually using the supplied appsettings.json template, then restart.");
                    }
                }
            }
        }
    }

    private static bool IsObsoleteSection(string name) =>
        string.Equals(name, "SqlServer", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "MetadataProvider", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "AnonymizationRules", StringComparison.OrdinalIgnoreCase);

    private static JsonElement? GetPropertyCaseInsensitive(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                return prop.Value;
        }
        return null;
    }

    private static void ValidateLoggingOptions(LoggingOptions logging, string settingsPath)
    {
        ValidateSinkRetention(logging.AppLog, "SqlToAi.Logging.AppLog.RetainedFileCount", settingsPath);
        ValidateSinkRetention(logging.ErrorLog, "SqlToAi.Logging.ErrorLog.RetainedFileCount", settingsPath);
    }

    private static void ValidateSchemaBatchOptions(SchemaBatchOptions options, string settingsPath)
    {
        if (options.MaxInputCount <= 0 || options.MaxResponseBytes <= 0)
        {
            throw new ConfigurationStartupException(
                $"SqlToAi.SchemaBatch.MaxInputCount and SqlToAi.SchemaBatch.MaxResponseBytes must be greater than zero. " +
                $"Settings file: '{settingsPath}'. Correct these values manually using the supplied appsettings.json template, then restart.");
        }
    }

    private static void ValidateSinkRetention(FileLogSinkOptions sink, string settingName, string settingsPath)
    {
        if (sink.Enabled && sink.RetainedFileCount <= 0)
        {
            throw new ConfigurationStartupException(
                $"{settingName} must be greater than zero when its file sink is enabled (configured value: {sink.RetainedFileCount}). " +
                $"Settings file: '{settingsPath}'. Correct this value manually using the supplied appsettings.json template, then restart.");
        }
    }

    internal static void EnsureWritableDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            using var probe = new FileStream(Path.Combine(directory, $".sqltoai-write-probe-{Guid.NewGuid():N}"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationStartupException(
                $"User data/log directory '{directory}' is not writable ({exception.GetType().Name}). " +
                "Check the configured path and write permissions, then restart.", exception);
        }
    }

    public void Dispose() => (Configuration as IDisposable)?.Dispose();
}

internal sealed class ConfigurationStartupException(string message, Exception? innerException = null) : Exception(message, innerException);
