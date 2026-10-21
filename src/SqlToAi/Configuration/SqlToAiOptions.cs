namespace SqlToAi.Configuration;

/// <summary>
/// Root configuration options for the SqlToAi application, combining database, anonymization, and metadata provider settings.
/// </summary>
public sealed class SqlToAiOptions
{
    internal string? ResolvedRootDirectory { get; set; }
    public Dictionary<string, NamedServerOptions> Servers { get; set; } = new(StringComparer.Ordinal);
    public DatabasesOptions Databases { get; set; } = new();
    public AnonymizerOptions Anonymizer { get; set; } = new();
    public QueryExecutionOptions QueryExecution { get; set; } = new();
    public SchemaBatchOptions SchemaBatch { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();
}

/// <summary>Connection and supporting-provider settings owned by one named SQL Server alias.</summary>
public sealed class NamedServerOptions
{
    public string Server { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Password { get; set; }
    public bool IntegratedSecurity { get; set; }
    public bool TrustServerCertificate { get; set; }
    public List<string> ExcludedDatabases { get; set; } = [];
    public int ConnectTimeoutSeconds { get; set; } = 30;
    public MetadataProviderOptions MetadataProvider { get; set; } = new() { Enabled = false };
    public AnonymizationRulesOptions AnonymizationRules { get; set; } = new();
}

public sealed class SchemaBatchOptions
{
    public int MaxInputCount { get; set; } = 10;
    public int MaxResponseBytes { get; set; } = 65_536;
}

/// <summary>
/// Options for database level authorization and access configuration.
/// </summary>
public sealed class DatabasesOptions
{
    public List<string> ReadWrite { get; set; } = [];
    public List<string> ReadWriteAnonymized { get; set; } = [];
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
/// excluded (<c>Anonymize == false</c>) for the resolved database/schema/table/column. The central
/// rule table is the source of exclusion decisions. Per-database opt-out is configured via the
/// access level (<c>ReadOnly</c>/<c>ReadWrite</c> for raw results, or their anonymized variants).
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
/// Connection settings shared by the per-server supporting-provider options
/// (<see cref="MetadataProviderOptions"/> and <see cref="AnonymizationRulesOptions"/>). A provider
/// slice with an empty <see cref="Server"/> uses the owning server's primary endpoint; a non-empty
/// <see cref="Server"/> is a dedicated source that must also carry its own <see cref="Database"/>.
/// </summary>
internal interface IProviderConnectionOptions
{
    string Server { get; }
    string? Database { get; }
    string? UserId { get; }
    string? Password { get; }
    bool IntegratedSecurity { get; }
    bool TrustServerCertificate { get; }
    int CommandTimeoutSeconds { get; }
}

/// <summary>
/// Options for the optional central, cross-database anonymization rule table. Its connection
/// and <see cref="TableName"/> are configured under the owning
/// <see cref="NamedServerOptions.AnonymizationRules"/> settings. The table can live in a
/// dedicated database so its rules apply consistently across customer databases via wildcard
/// patterns.
/// </summary>
public sealed class AnonymizationRulesOptions : IProviderConnectionOptions
{
    /// <summary>Master switch. When false, the rule table is never queried.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// SQL Server instance hosting the rule table. When empty, falls back to the owner's primary
    /// <see cref="NamedServerOptions.Server"/> connection.
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>Database hosting the rule table. Required when <see cref="Server"/> is set.</summary>
    public string? Database { get; set; }

    public string? UserId { get; set; }
    public string? Password { get; set; }
    public bool IntegratedSecurity { get; set; }
    public bool TrustServerCertificate { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Name of the rule table, e.g. <c>dbo.AnonymizationRules</c>.</summary>
    public string TableName { get; set; } = "dbo.AnonymizationRules";

    /// <summary>How long the full rule set is cached in memory before being reloaded.</summary>
    public int CacheTtlSeconds { get; set; } = 300;
}

/// <summary>
/// Options for enriching database schemas with custom query-based documentation.
/// </summary>
public sealed class MetadataProviderOptions : IProviderConnectionOptions
{
    public bool Enabled { get; set; } = true;
    public string Server { get; set; } = string.Empty;
    public string? Database { get; set; }
    public string? UserId { get; set; }
    public string? Password { get; set; }
    public bool IntegratedSecurity { get; set; }
    public bool TrustServerCertificate { get; set; }
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
    /// Command timeout in seconds for query/script execution, validation, comparison and
    /// performance measurement, including their SET and transaction-integrity commands.
    /// Zero leaves ordinary commands unlimited. Cleanup uses the default timeout when zero is
    /// configured and has independent cancellation; rollback cancellation is provider-dependent.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum number of bytes accepted from a local SQL script file.</summary>
    public long MaxScriptFileSizeBytes { get; set; } = 10_485_760L;
}
