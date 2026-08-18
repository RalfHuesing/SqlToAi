#nullable enable

using RalfHuesing.Mcp.Observability;

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
    public McpObservabilityOptions Observability { get; set; } = new();
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

    /// <summary>ADO.NET connection (login) timeout in seconds, i.e. how long to wait when opening the connection.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Options for database level authorization and access configuration.
/// </summary>
public sealed class DatabasesOptions
{
    public List<string> ReadWrite { get; set; } = [];
    public List<string> ReadOnly { get; set; } = [];
    public List<string> ReadOnlyAnonymized { get; set; } = [];
    public List<string> SchemaOnly { get; set; } = [];
    public int CacheTtlSeconds { get; set; } = 300;
}

/// <summary>
/// Options for string value anonymization to protect PII (Personally Identifiable Information).
/// <para>
/// Default behavior: every string column is anonymized with <see cref="DefaultMode"/> unless the
/// central <c>AnonymizationRules</c> table (see <c>AnonymizationRuleProvider</c>) marks it as
/// excluded (<c>Anonymize == false</c>) for the resolved database/schema/table/column. There is
/// no local, options-based exclusion list anymore (see <c>AnonymizerExclusionProvider</c>
/// removal, 2026-07-25) — the central rule table is the single source of truth. Per-database
/// opt-out is configured via the database access level (<c>Databases.ReadOnly</c> for raw versus
/// <c>Databases.ReadOnlyAnonymized</c>).
/// </para>
/// </summary>
public sealed class AnonymizerOptions
{
    /// <summary>Master switch. When false, <see cref="Anonymizer.Anonymize"/> returns the input unchanged.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Algorithm used for anonymization. One of <c>ScramblePattern</c> (default) or <c>Hash</c>.</summary>
    public string DefaultMode { get; set; } = "ScramblePattern";

    /// <summary>Options for reversible, searchable tokenization (see <see cref="TokenizationOptions"/>).</summary>
    public TokenizationOptions Tokenization { get; set; } = new();
}

/// <summary>
/// Options for reversible, searchable tokenization. When usable (see <see cref="IsUsable"/>), this
/// replaces <see cref="AnonymizerOptions.DefaultMode"/> as the anonymization mode for every string
/// column that would otherwise be anonymized — a global mode switch, exactly like
/// <see cref="AnonymizerOptions.DefaultMode"/> itself, not a per-column opt-in. The anonymized value
/// handed to the AI becomes a deterministic, keyed token instead of a scrambled/hashed mask. The
/// server remembers the token-to-value mapping in memory for the lifetime of the process, so a later
/// query that reuses the very same token (e.g. in a <c>WHERE</c>, <c>JOIN</c>, or <c>LIKE</c>) is
/// transparently resolved back to the real value before execution — the AI never learns the real
/// value itself, only that two tokens refer to the same underlying row.
/// <para>
/// Which columns get anonymized at all is unaffected by this setting — that decision still runs
/// entirely through the central <c>AnonymizationRules</c> table.
/// This setting only changes *how* an already-anonymized column is anonymized.
/// </para>
/// </summary>
public sealed class TokenizationOptions
{
    /// <summary>Master switch. When false, every anonymized column keeps using the regular <see cref="AnonymizerOptions.DefaultMode"/> masking.</summary>
    public bool Enabled { get; set; }

    /// <summary>Marker prepended to every token so it can be unambiguously recognized in SQL text.</summary>
    public string Prefix { get; set; } = "§§§";

    /// <summary>Marker appended to every token so it can be unambiguously recognized in SQL text.</summary>
    public string Suffix { get; set; } = "§§§";

    /// <summary>
    /// Whether tokenization is actually usable, i.e. enabled with non-empty
    /// delimiters. Both <see cref="Anonymizer"/> and the query-side token resolver must agree on this
    /// exact condition, so it lives here as the single source of truth.
    /// </summary>
    public bool IsUsable =>
        Enabled
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

    /// <summary>
    /// Command execution timeout in seconds applied to every query run via
    /// <c>sql_execute_query</c>. Also used by <see cref="SqlToAi.Database.QueryValidationService"/>
    /// (<c>sql_validate_query</c>) for its <c>SET NOEXEC</c> parse-only validation commands,
    /// since both run the same kind of <see cref="System.Data.Common.DbCommand"/> against the
    /// same connection for the same purpose (bounding how long a not-yet-fully-executed query
    /// may run).
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
