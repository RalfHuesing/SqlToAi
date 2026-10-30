using System;
using System.Collections.Generic;
using System.IO;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Configuration;

/// <summary>
/// Resolves configuration options by expanding environment variables and loading SQL file contents.
/// </summary>
public static class ConfigurationResolver
{
    /// <summary>
    /// Processes the given <see cref="SqlToAiOptions"/> by expanding environment variables
    /// in string settings and loading SQL query content from files.
    /// </summary>
    /// <param name="options">The options to process.</param>
    public static void Resolve(SqlToAiOptions? options)
    {
        if (options is null) return;
        Resolve(options, options.ResolvedRootDirectory ?? UserConfiguration.ResolveRoot());
    }

    internal static void Resolve(SqlToAiOptions? options, string rootDirectory)
    {
        if (options == null) return;
        string root = Path.GetFullPath(rootDirectory);
        if (options.ResolvedRootDirectory is not null)
        {
            if (!string.Equals(root, options.ResolvedRootDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Options have already been resolved against a different user root.");
            return;
        }

        // 1. Expand environment variables
        ExpandEnvironmentVariables(options);

        // 2. Resolve SQL file paths
        ResolveSqlFiles(options, root);
        options.Logging.Directory = ResolvePath(options.Logging.Directory, root);
        options.ResolvedRootDirectory = root;
    }

    private static string ResolvePath(string path, string root) => Path.GetFullPath(Expand(path), root);

    private static void ExpandEnvironmentVariables(SqlToAiOptions options)
    {
        if (options.Databases != null)
        {
            ExpandListInPlace(options.Databases.ReadWrite);
            ExpandListInPlace(options.Databases.ReadWriteAnonymized);
            ExpandListInPlace(options.Databases.ReadOnly);
            ExpandListInPlace(options.Databases.ReadOnlyAnonymized);
            ExpandListInPlace(options.Databases.SchemaOnly);
        }

        if (options.Anonymizer != null)
        {
            if (options.Anonymizer.Tokenization != null)
            {
                options.Anonymizer.Tokenization.Prefix = Expand(options.Anonymizer.Tokenization.Prefix);
                options.Anonymizer.Tokenization.Suffix = Expand(options.Anonymizer.Tokenization.Suffix);
            }
        }

        foreach (NamedServerOptions server in options.Servers.Values)
        {
            ExpandNamedServer(server);
        }
    }

    private static void ExpandNamedServer(NamedServerOptions server)
    {
        server.Server = Expand(server.Server);
        server.UserId = Expand(server.UserId);
        server.Password = Expand(server.Password);

        if (server.MetadataProvider != null)
        {
            server.MetadataProvider.Server = Expand(server.MetadataProvider.Server);
            server.MetadataProvider.Database = Expand(server.MetadataProvider.Database);
            server.MetadataProvider.UserId = Expand(server.MetadataProvider.UserId);
            server.MetadataProvider.Password = Expand(server.MetadataProvider.Password);
            server.MetadataProvider.TableMetadataQuery = Expand(server.MetadataProvider.TableMetadataQuery);
            server.MetadataProvider.ColumnMetadataQuery = Expand(server.MetadataProvider.ColumnMetadataQuery);
        }

        if (server.AnonymizationRules != null)
        {
            server.AnonymizationRules.Server = Expand(server.AnonymizationRules.Server);
            server.AnonymizationRules.Database = Expand(server.AnonymizationRules.Database);
            server.AnonymizationRules.UserId = Expand(server.AnonymizationRules.UserId);
            server.AnonymizationRules.Password = Expand(server.AnonymizationRules.Password);
        }
    }

    private static void ResolveSqlFiles(SqlToAiOptions options, string root)
    {
        foreach (NamedServerOptions server in options.Servers.Values)
        {
            if (server.MetadataProvider != null)
            {
                server.MetadataProvider.TableMetadataQuery = ResolveValue(server.MetadataProvider.TableMetadataQuery, root);
                server.MetadataProvider.ColumnMetadataQuery = ResolveValue(server.MetadataProvider.ColumnMetadataQuery, root);
            }
        }
    }

    public static void Validate(SqlToAiOptions options, string? settingsPath = null)
    {
        string pathSuffix = settingsPath is not null
            ? $" Settings file: '{settingsPath}'. Correct this manually using the supplied appsettings.json template, then restart."
            : " Correct this manually using the supplied appsettings.json template, then restart.";

        if (options.Servers == null || options.Servers.Count == 0)
        {
            throw new ConfigurationStartupException(
                $"At least one named server must be configured under 'SqlToAi.Servers'.{pathSuffix}");
        }

        var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, server) in options.Servers)
        {
            if (!DatabaseTargetParser.IsValidAlias(alias))
            {
                throw new ConfigurationStartupException(
                    $"Server alias '{alias}' is invalid. Server aliases must match [A-Za-z][A-Za-z0-9_-]*.{pathSuffix}");
            }

            if (!seenAliases.Add(alias))
            {
                throw new ConfigurationStartupException(
                    $"Duplicate server alias '{alias}' in SqlToAi.Servers. Server aliases must be unique (case-insensitive).{pathSuffix}");
            }

            if (string.IsNullOrWhiteSpace(server.Server))
            {
                throw new ConfigurationStartupException(
                    $"Server '{alias}' has an empty or missing 'Server' address.{pathSuffix}");
            }
        }

        ValidateGrants("SqlToAi.Databases.ReadWrite", options.Databases.ReadWrite, options.Servers, pathSuffix);
        ValidateGrants("SqlToAi.Databases.ReadWriteAnonymized", options.Databases.ReadWriteAnonymized, options.Servers, pathSuffix);
        ValidateGrants("SqlToAi.Databases.ReadOnly", options.Databases.ReadOnly, options.Servers, pathSuffix);
        ValidateGrants("SqlToAi.Databases.ReadOnlyAnonymized", options.Databases.ReadOnlyAnonymized, options.Servers, pathSuffix);
        ValidateGrants("SqlToAi.Databases.SchemaOnly", options.Databases.SchemaOnly, options.Servers, pathSuffix);

        ValidateSinkRetention(options.Logging.AppLog, "SqlToAi.Logging.AppLog.RetainedFileCount", pathSuffix);
        ValidateSinkRetention(options.Logging.ErrorLog, "SqlToAi.Logging.ErrorLog.RetainedFileCount", pathSuffix);
        if (options.SchemaBatch.MaxInputCount <= 0 || options.SchemaBatch.MaxResponseBytes <= 0)
        {
            throw new ConfigurationStartupException(
                $"SqlToAi.SchemaBatch.MaxInputCount and SqlToAi.SchemaBatch.MaxResponseBytes must be greater than zero.{pathSuffix}");
        }

        if (options.QueryExecution.Export.DefaultRowLimit <= 0 || options.QueryExecution.Export.MaxRowLimit <= 0)
        {
            throw new ConfigurationStartupException(
                $"SqlToAi.QueryExecution.Export.DefaultRowLimit and SqlToAi.QueryExecution.Export.MaxRowLimit must be greater than zero.{pathSuffix}");
        }
    }

    private static void ValidateSinkRetention(FileLogSinkOptions sink, string settingName, string pathSuffix)
    {
        if (sink.Enabled && sink.RetainedFileCount <= 0)
        {
            throw new ConfigurationStartupException(
                $"{settingName} must be greater than zero when its file sink is enabled (configured value: {sink.RetainedFileCount}).{pathSuffix}");
        }
    }

    private static void ValidateGrants(string sectionName, List<string>? grants, Dictionary<string, NamedServerOptions> servers, string pathSuffix)
    {
        if (grants == null) return;
        foreach (string grant in grants)
        {
            var parsed = DatabaseTargetParser.Parse(grant);
            if (parsed.IsFailure)
            {
                throw new ConfigurationStartupException(
                    $"Database grant '{grant}' in {sectionName} is invalid: {parsed.Error.Message} Every database grant must be qualified in 'alias/database' form.{pathSuffix}");
            }

            if (!servers.Keys.Any(s => string.Equals(s, parsed.Value.ServerAlias, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ConfigurationStartupException(
                    $"Database grant '{grant}' in {sectionName} references unknown server alias '{parsed.Value.ServerAlias}'.{pathSuffix}");
            }
        }
    }

    private static string Expand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value ?? string.Empty;
        }
        string expanded = Environment.ExpandEnvironmentVariables(value);
        if (expanded.Contains("%COMPUTERNAME%", StringComparison.OrdinalIgnoreCase))
        {
            expanded = expanded.Replace("%COMPUTERNAME%", Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        }
        return expanded;
    }

    private static void ExpandListInPlace(List<string>? list)
    {
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            list[i] = Expand(list[i]);
        }
    }

    private static string ResolveValue(string value, string root)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        string trimmedValue = value.Trim();

        if (trimmedValue.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            string fullPath = Path.GetFullPath(trimmedValue, root);

            if (File.Exists(fullPath))
            {
                return File.ReadAllText(fullPath);
            }
            else
            {
                throw new FileNotFoundException($"SQL file configuration error: The file '{trimmedValue}' (resolved to '{fullPath}') was not found.", fullPath);
            }
        }

        return value;
    }
}
