#nullable enable

namespace SqlToAi.Configuration;

/// <summary>
/// Root configuration options for the SqlToAi application, combining database, anonymization, and metadata provider settings.
/// </summary>
public sealed class SqlToAiOptions
{
    public SqlServerOptions SqlServer { get; set; } = new();
    public DatabasesOptions Databases { get; set; } = new();
    public AnonymizerOptions Anonymizer { get; set; } = new();
    public AnonymizationRulesOptions AnonymizationRules { get; set; } = new();
    public MetadataProviderOptions MetadataProvider { get; set; } = new();
    public QueryExecutionOptions QueryExecution { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();
}

/// <summary>
/// Options specifically for the SQL Server connection credentials and global safety checks.
/// </summary>
public sealed class SqlServerOptions
{
    public string Server { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Password { get; set; }
    public bool IntegratedSecurity { get; set; }
    public List<string> ExcludedDatabases { get; set; } = [];
    public int CommandTimeoutSeconds { get; set; } = 30;
    public bool EnforceSafetyCheck { get; set; } = true;
    public string SafetyCheckSql { get; set; } = string.Empty;
}

/// <summary>
/// Options for static database whitelisting and dynamic access verification checks.
/// </summary>
public sealed class DatabasesOptions
{
    public List<string> Allowed { get; set; } = [];
    public List<string> Blocked { get; set; } = [];
    public int CacheTtlSeconds { get; set; } = 300;
    public string AccessCheckSql { get; set; } = string.Empty;
    public string AnonymizerExclusionSql { get; set; } = string.Empty;
}

/// <summary>
/// Options for string value anonymization to protect PII (Personally Identifiable Information).
/// <para>
/// Default behavior: every string column is anonymized with <see cref="DefaultMode"/> unless its
/// name matches one of the <see cref="ExcludedColumns"/> glob patterns. Per-database
/// opt-out is configured via the dynamic <c>AccessCheckSql</c> returning
/// <c>ReadOnly</c> (raw) versus <c>ReadOnlyAnonymized</c>.
/// </para>
/// </summary>
public sealed class AnonymizerOptions
{
    /// <summary>Master switch. When false, <see cref="Anonymizer.Anonymize"/> returns the input unchanged.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Algorithm used for anonymization. One of <c>ScramblePattern</c> (default) or <c>Hash</c>.</summary>
    public string DefaultMode { get; set; } = "ScramblePattern";

    /// <summary>
    /// Glob patterns for column names that must NOT be anonymized (e.g. <c>*Id</c>, <c>*Code</c>,
    /// <c>Status</c>). Use sparingly — anything not listed here is anonymized by default.
    /// </summary>
    public List<string> ExcludedColumns { get; set; } = [];

    /// <summary>
    /// Optional central table name containing table/column exemptions.
    /// The table must contain the columns <c>TableName</c> and <c>ColumnName</c>.
    /// </summary>
    public string? ExclusionTableName { get; set; }

    /// <summary>Options for reversible, searchable tokenization (see <see cref="TokenizationOptions"/>).</summary>
    public TokenizationOptions Tokenization { get; set; } = new();
}

/// <summary>
/// Options for reversible, searchable tokenization: for columns explicitly marked as
/// <c>SearchableToken</c> (via <see cref="AnonymizationRulesOptions"/> or <see cref="SearchableColumns"/>),
/// the anonymized value handed to the AI is a deterministic, keyed token instead of a scrambled/hashed
/// mask. The server remembers the token-to-value mapping in memory for the lifetime of the process, so
/// a later query that reuses the very same token (e.g. in a <c>WHERE</c>, <c>JOIN</c>, or <c>LIKE</c>)
/// is transparently resolved back to the real value before execution — the AI never learns the real
/// value itself, only that two tokens refer to the same underlying row.
/// <para>
/// This is a stricter, opt-in mode layered on top of <see cref="AnonymizerOptions"/>: it only ever
/// applies to columns explicitly flagged as searchable, everything else keeps using
/// <see cref="AnonymizerOptions.DefaultMode"/> as before.
/// </para>
/// </summary>
public sealed class TokenizationOptions
{
    /// <summary>Master switch. When false, flagged columns fall back to the regular <see cref="AnonymizerOptions.DefaultMode"/> masking.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Secret key for the deterministic HMAC-SHA256 token derivation. Required for tokenization to be
    /// usable — when empty, <see cref="Enabled"/> is effectively ignored and flagged columns fall back
    /// to regular masking (fail-safe default). Never hardcode this; supply it via an environment
    /// variable placeholder (e.g. <c>%SQLTOAI_TOKEN_SECRET%</c>).
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Marker prepended to every token so it can be unambiguously recognized in SQL text.</summary>
    public string Prefix { get; set; } = "§§§";

    /// <summary>Marker appended to every token so it can be unambiguously recognized in SQL text.</summary>
    public string Suffix { get; set; } = "§§§";

    /// <summary>
    /// Glob patterns for column names that should use searchable tokenization instead of regular
    /// masking, evaluated in addition to the central <c>AnonymizationRules.SearchableToken</c> flag.
    /// Empty by default — nothing is searchable unless explicitly listed here or via a rule.
    /// </summary>
    public List<string> SearchableColumns { get; set; } = [];

    /// <summary>
    /// Whether tokenization is actually usable, i.e. enabled with a non-empty secret and non-empty
    /// delimiters. Both <see cref="Anonymizer"/> and the query-side token resolver must agree on this
    /// exact condition, so it lives here as the single source of truth.
    /// </summary>
    public bool IsUsable =>
        Enabled
        && !string.IsNullOrEmpty(Secret)
        && !string.IsNullOrEmpty(Prefix)
        && !string.IsNullOrEmpty(Suffix);
}

/// <summary>
/// Options for the optional central, cross-database anonymization rule table. Unlike
/// <see cref="AnonymizerOptions.ExclusionTableName"/> (which lives inside each customer
/// database and is wiped out by a customer backup restore), this table is intended to live
/// in its own dedicated database — configured independently of any customer connection — so
/// its rules survive customer-side restores and apply consistently across many customer
/// databases via wildcard patterns.
/// </summary>
public sealed class AnonymizationRulesOptions
{
    /// <summary>Master switch. When false, the rule table is never queried.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// SQL Server instance hosting the rule table. When empty, falls back to the standard
    /// <see cref="SqlServerOptions.Server"/> connection.
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>Database hosting the rule table. Required when <see cref="Server"/> is set.</summary>
    public string? Database { get; set; }

    public string? UserId { get; set; }
    public string? Password { get; set; }
    public bool IntegratedSecurity { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Name of the rule table, e.g. <c>dbo.AnonymizationRules</c>.</summary>
    public string TableName { get; set; } = "dbo.AnonymizationRules";

    /// <summary>How long the full rule set is cached in memory before being reloaded.</summary>
    public int CacheTtlSeconds { get; set; } = 300;
}

/// <summary>
/// Options for enriching database schemas with custom query-based documentation.
/// </summary>
public sealed class MetadataProviderOptions
{
    public bool Enabled { get; set; } = true;
    public string Server { get; set; } = string.Empty;
    public string? Database { get; set; }
    public string? UserId { get; set; }
    public string? Password { get; set; }
    public bool IntegratedSecurity { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 30;
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
