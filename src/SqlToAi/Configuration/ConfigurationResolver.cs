#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

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
        if (options == null) return;

        // 1. Expand environment variables
        ExpandEnvironmentVariables(options);

        // 2. Resolve SQL file paths
        ResolveSqlFiles(options);
    }

    private static void ExpandEnvironmentVariables(SqlToAiOptions options)
    {
        if (options.SqlServer != null)
        {
            options.SqlServer.Server = Expand(options.SqlServer.Server);
            options.SqlServer.UserId = Expand(options.SqlServer.UserId);
            options.SqlServer.Password = Expand(options.SqlServer.Password);
            options.SqlServer.SafetyCheckSql = Expand(options.SqlServer.SafetyCheckSql);
        }

        if (options.Databases != null)
        {
            options.Databases.AccessCheckSql = Expand(options.Databases.AccessCheckSql);
            ExpandListInPlace(options.Databases.Allowed);
            ExpandListInPlace(options.Databases.Blocked);
        }

        if (options.MetadataProvider != null)
        {
            options.MetadataProvider.Server = Expand(options.MetadataProvider.Server);
            options.MetadataProvider.UserId = Expand(options.MetadataProvider.UserId);
            options.MetadataProvider.Password = Expand(options.MetadataProvider.Password);
            options.MetadataProvider.TableMetadataQuery = Expand(options.MetadataProvider.TableMetadataQuery);
            options.MetadataProvider.ColumnMetadataQuery = Expand(options.MetadataProvider.ColumnMetadataQuery);
        }

        if (options.Anonymizer != null)
        {
            ExpandListInPlace(options.Anonymizer.ExcludedColumns);
        }

        if (options.Logging != null)
        {
            options.Logging.Directory = Expand(options.Logging.Directory);
            if (options.Logging.McpTrail != null)
            {
                options.Logging.McpTrail.Directory = Expand(options.Logging.McpTrail.Directory);
            }
        }
    }

    private static void ResolveSqlFiles(SqlToAiOptions options)
    {
        if (options.SqlServer != null)
        {
            options.SqlServer.SafetyCheckSql = ResolveValue(options.SqlServer.SafetyCheckSql);
        }

        if (options.Databases != null)
        {
            options.Databases.AccessCheckSql = ResolveValue(options.Databases.AccessCheckSql);
        }

        if (options.MetadataProvider != null)
        {
            options.MetadataProvider.TableMetadataQuery = ResolveValue(options.MetadataProvider.TableMetadataQuery);
            options.MetadataProvider.ColumnMetadataQuery = ResolveValue(options.MetadataProvider.ColumnMetadataQuery);
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

    private static string ResolveValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        string trimmedValue = value.Trim();

        if (trimmedValue.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            string fullPath = Path.IsPathRooted(trimmedValue)
                ? trimmedValue
                : Path.Combine(AppContext.BaseDirectory, trimmedValue);

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
