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
            // Open before constructing providers: even a nonexistent root must fail without creation.
            using var baseFile = File.OpenRead(expectedPath);
            configuration = new ConfigurationBuilder()
                .SetBasePath(root)
                .AddJsonStream(baseFile)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json",
                    optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();
            var options = configuration.GetSection("SqlToAi").Get<SqlToAiOptions>() ?? new SqlToAiOptions();
            ConfigurationResolver.Resolve(options);
            return new UserConfiguration(root, configuration, options);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or JsonException)
        {
            (configuration as IDisposable)?.Dispose();
            string reason = exception switch
            {
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

    public void Dispose() => (Configuration as IDisposable)?.Dispose();
}

internal sealed class ConfigurationStartupException(string message, Exception innerException) : Exception(message, innerException);
