#nullable enable

namespace SqlToAi.Configuration;

/// <summary>
/// Root configuration options for the SqlToAi application, combining database, anonymization, and metadata provider settings.
/// </summary>
public sealed class SqlToAiOptions
{
    public SqlDatabaseOptions SqlDatabase { get; set; } = new();
    public DatabasesOptions Databases { get; set; } = new();
    public AnonymizerOptions Anonymizer { get; set; } = new();
    public MetadataProviderOptions MetadataProvider { get; set; } = new();
    public QueryExecutionOptions QueryExecution { get; set; } = new();
}

/// <summary>
/// Options specifically for the SQL Server connection credentials and global safety checks.
/// </summary>
public sealed class SqlDatabaseOptions
{
    public string Server { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Password { get; set; }
    public string DefaultDatabase { get; set; } = string.Empty;
    public List<string> ExcludedDatabases { get; set; } = [];
    public int CommandTimeoutSeconds { get; set; } = 30;
    public bool EnforceSafetyCheck { get; set; } = true;
    public string SafetyCheckSql { get; set; } = string.Empty;
    public bool ReadOnly { get; set; } = true;
}

/// <summary>
/// Options for static database whitelisting and dynamic access verification checks.
/// </summary>
public sealed class DatabasesOptions
{
    public string Default { get; set; } = string.Empty;
    public List<string> Allowed { get; set; } = [];
    public List<string> Blocked { get; set; } = [];
    public int CacheTtlSeconds { get; set; } = 300;
    public string AccessCheckSql { get; set; } = string.Empty;
}

/// <summary>
/// Options for string value anonymization to protect PII (Personally Identifiable Information).
/// </summary>
public sealed class AnonymizerOptions
{
    public bool Enabled { get; set; } = true;
    public string DefaultMode { get; set; } = "ScramblePattern";
    public string Mode { get; set; } = "ScramblePattern"; // Legacy/Fallback alias
    public List<AnonymizerRule> Rules { get; set; } = [];
    public List<string> ExcludedColumns { get; set; } = [];
}

/// <summary>
/// Represents a rule mapping a column pattern to an anonymization mode.
/// </summary>
public sealed class AnonymizerRule
{
    public string Pattern { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
}

/// <summary>
/// Options for enriching database schemas with custom query-based documentation.
/// </summary>
public sealed class MetadataProviderOptions
{
    public bool Enabled { get; set; } = true;
    public string ConnectionString { get; set; } = string.Empty;
    public string TableMetadataQuery { get; set; } = string.Empty;
    public string ColumnMetadataQuery { get; set; } = string.Empty;
}

/// <summary>
/// Options for safe query execution: row limits and statement validation.
/// </summary>
public sealed class QueryExecutionOptions
{
    /// <summary>Default number of rows returned when the caller does not specify a limit.</summary>
    public int DefaultRowLimit { get; set; } = 100;

    /// <summary>Hard ceiling on rows returned regardless of caller request.</summary>
    public int MaxRowLimit { get; set; } = 1000;
}
