#nullable enable

using System;
using System.IO;

namespace SqlToAi.Configuration;

/// <summary>
/// Resolves configuration strings that point to SQL files by reading their contents from disk.
/// </summary>
public static class SqlFileResolver
{
    /// <summary>
    /// Checks specific properties in <see cref="SqlToAiOptions"/> and replaces their values with
    /// SQL file content if they refer to a file path ending in ".sql".
    /// </summary>
    /// <param name="options">The configurations option to process.</param>
    /// <exception cref="FileNotFoundException">Thrown when a SQL file path is specified but the file does not exist.</exception>
    public static void Resolve(SqlToAiOptions? options)
    {
        if (options == null) return;

        if (options.SqlDatabase != null)
        {
            options.SqlDatabase.SafetyCheckSql = ResolveValue(options.SqlDatabase.SafetyCheckSql);
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
