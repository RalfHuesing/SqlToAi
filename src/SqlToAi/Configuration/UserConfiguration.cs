using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace SqlToAi.Configuration;

internal sealed class UserConfiguration : IDisposable
{
    private static readonly string[] ObsoleteSectionNames = ["SqlServer", "MetadataProvider", "AnonymizationRules"];

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
                string normalizedPath = key.Replace("__", ":", StringComparison.Ordinal);
                if (!normalizedPath.StartsWith("SqlToAi:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? obsoleteSection = GetObsoleteSectionPrefix(normalizedPath);
                if (obsoleteSection is not null && obsoleteSection.StartsWith("SqlToAi.", StringComparison.Ordinal))
                {
                    string sectionName = obsoleteSection["SqlToAi.".Length..];
                    throw new ConfigurationStartupException(
                        $"Obsolete configuration environment setting 'SqlToAi:{sectionName}' detected. Obsolete single-server settings ('SqlServer', 'MetadataProvider', 'AnonymizationRules') have been removed in favor of 'SqlToAi.Servers.<alias>'. " +
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

        InspectJsonElement(document.RootElement, null, filePath, settingsPath);
    }

    private static void InspectJsonElement(JsonElement element, string? path, string filePath, string settingsPath)
    {
        string? obsoleteSection = GetObsoleteSectionPrefix(path);
        if (obsoleteSection is not null)
        {
            throw new ConfigurationStartupException(
                $"Obsolete configuration section '{obsoleteSection}' detected in '{filePath}'. Obsolete single-server settings ('SqlServer', 'MetadataProvider', 'AnonymizationRules') have been removed in favor of 'SqlToAi.Servers.<alias>'. " +
                $"Settings file: '{settingsPath}'. Correct this manually using the supplied appsettings.json template, then restart.");
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            bool isServersObject = string.Equals(path, "SqlToAi:Servers", StringComparison.OrdinalIgnoreCase);
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (isServersObject)
                {
                    int separator = property.Name.IndexOf(':');
                    string alias = separator < 0 ? property.Name : property.Name[..separator];
                    // Same-spelling flattened fields can configure one alias. A repeated
                    // declaration or another spelling must not be hidden by provider merging.
                    if (!propertyNames.Add(property.Name)
                        || aliases.TryGetValue(alias, out string? firstAlias)
                            && !string.Equals(firstAlias, alias, StringComparison.Ordinal))
                    {
                        throw new ConfigurationStartupException(
                            $"Duplicate server alias detected in '{filePath}' under 'SqlToAi.Servers'. Server aliases must be unique (case-insensitive). " +
                            $"Settings file: '{settingsPath}'. Correct this manually using the supplied appsettings.json template, then restart.");
                    }
                    aliases.TryAdd(alias, alias);
                }

                // JsonConfigurationProvider treats ':' within a property name as a path delimiter.
                string propertyPath = path is null ? property.Name : $"{path}:{property.Name}";
                InspectJsonElement(property.Value, propertyPath, filePath, settingsPath);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var item in element.EnumerateArray())
            {
                InspectJsonElement(item, $"{path}:{index.ToString(CultureInfo.InvariantCulture)}", filePath, settingsPath);
                index++;
            }
        }
    }

    private static string? GetObsoleteSectionPrefix(string? path)
    {
        if (path is null) return null;
        foreach (string section in ObsoleteSectionNames)
        {
            if (IsPathPrefix(path, section)) return section;
            if (IsPathPrefix(path, $"SqlToAi:{section}")) return $"SqlToAi.{section}";
        }
        return null;
    }

    private static bool IsPathPrefix(string path, string prefix) =>
        string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith($"{prefix}:", StringComparison.OrdinalIgnoreCase);

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
