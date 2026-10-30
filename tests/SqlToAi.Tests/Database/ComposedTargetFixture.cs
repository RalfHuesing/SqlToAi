using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Cli;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Metadata;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

internal sealed class ComposedTargetFixture
{
    internal const string DataQuery = "SELECT Value, Secret FROM dbo.T";
    internal const string SharedSecret = "same-secret";

    internal ComposedTargetFixture(SqlToAiOptions? configuration = null)
    {
        Configuration = configuration ?? CreateConfiguration();
        var options = Options.Create(Configuration);
        Factory = new RecordingPhysicalBoundary(options);
        var security = new SecurityGuard(options);
        var access = new AccessLevelProvider(options);
        var safety = new QuerySafetyValidator(security, access, new ReadOnlyGuard());
        Vault = new TokenVault();
        var rules = new AnonymizationRuleProvider(Factory, options, NullLogger<AnonymizationRuleProvider>.Instance);
        var anonymizer = new Anonymizer(options, Vault);
        Query = new QueryExecutionService(Factory, safety,
            new AnonymizationDependencies(anonymizer, rules, new QueryTokenResolver(Vault, options)),
            options, NullLogger<QueryExecutionService>.Instance);
        var validation = new QueryValidationService(Factory, safety, options, NullLogger<QueryValidationService>.Instance);
        var script = new ScriptExecutionService(Factory, safety, Query, options, NullLogger<ScriptExecutionService>.Instance);
        var metadata = new MetadataProvider(Factory, options, NullLogger<MetadataProvider>.Instance);
        var schema = new SchemaService(Factory, security, access, metadata,
            new AnonymizationPolicyResolver(options, rules), options, NullLogger<SchemaService>.Instance);
        var dependencies = new QueryExecutionDependencies(Factory, safety, options);
        var comparison = new QueryComparisonService(dependencies, NullLogger<QueryComparisonService>.Instance);
        var performance = new PerformanceMeasurementService(dependencies, NullLogger<PerformanceMeasurementService>.Instance);
        var analysis = new DatabaseAnalysisServices(performance, comparison,
            new OptimizationBenchmarkService(comparison, performance, options),
            new IndexSuggestionService(Factory, security, access, options, NullLogger<IndexSuggestionService>.Instance));
        var exportData = new DataExportService(Factory, safety, new ReadOnlyGuard(),
            new AnonymizationDependencies(anonymizer, rules, new QueryTokenResolver(Vault, options)),
            options, NullLogger<DataExportService>.Instance);
        Dispatcher = new ToolDispatcher(schema, Query, script, validation, analysis, options,
            NullLogger<ToolDispatcher>.Instance, new SchemaExportService(schema, options, NullLogger<SchemaExportService>.Instance),
            exportData);
    }

    internal SqlToAiOptions Configuration { get; }
    internal RecordingPhysicalBoundary Factory { get; }
    internal TokenVault Vault { get; }
    internal QueryExecutionService Query { get; }
    internal ToolDispatcher Dispatcher { get; }

    internal static SqlToAiOptions CreateConfiguration(bool sameEndpoint = false)
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Tokenization.Enabled = true;
        options.Servers["ALPHA"] = new NamedServerOptions
        {
            Server = sameEndpoint ? "shared.invalid" : "alpha.invalid", IntegratedSecurity = !sameEndpoint,
            UserId = "synthetic-alpha", Password = "synthetic-alpha-password", ConnectTimeoutSeconds = 7,
            AnonymizationRules = new() { Enabled = true, CacheTtlSeconds = 300 }
        };
        options.Servers["beta"] = new NamedServerOptions
        {
            Server = sameEndpoint ? "shared.invalid" : "beta.invalid", IntegratedSecurity = false,
            UserId = "synthetic-beta", Password = "synthetic-beta-password", TrustServerCertificate = true,
            ConnectTimeoutSeconds = 19, AnonymizationRules = new() { Enabled = true, CacheTtlSeconds = 300 }
        };
        options.Databases.ReadOnlyAnonymized = ["alpha/SharedDb", "beta/SharedDb"];
        return options;
    }

    internal sealed record ConnectionObservation(string Owner, DatabaseTargetContext? Target, SqlConnectionStringBuilder Settings);
    internal sealed record CommandObservation(string Target, string Sql, CancellationToken Token);

    /// <summary>Exercises production connection construction before substituting the unopened physical boundary.</summary>
    internal sealed class RecordingPhysicalBoundary(IOptions<SqlToAiOptions> options) : IDatabaseConnectionFactory
    {
        private readonly SqlConnectionFactory _production = new(options);
        private readonly DatabaseTargetResolver _targets = new(options.Value);

        internal ConcurrentQueue<ConnectionObservation> Connections { get; } = new();
        internal ConcurrentQueue<CommandObservation> Queries { get; } = new();
        internal ConcurrentDictionary<string, int> RuleLoads { get; } = new(StringComparer.Ordinal);
        internal TaskCompletionSource<string> RuleLoadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, bool> ExemptValues { get; } = new(StringComparer.Ordinal)
        {
            ["alpha/SharedDb"] = true, ["beta/SharedDb"] = false
        };
        internal HashSet<string> FailedRuleSources { get; } = [];
        internal Dictionary<string, string[]> Catalogs { get; } = new(StringComparer.Ordinal)
        {
            ["alpha"] = ["SharedDb", "HiddenDb"], ["beta"] = ["SharedDb", "HiddenDb"]
        };
        internal OwnerOverlapBarrier? ColdRuleBarrier { get; set; }

        public DbConnection CreateTargetConnection(DatabaseTargetContext target)
        {
            using var physical = _production.CreateTargetConnection(target);
            var settings = new SqlConnectionStringBuilder(physical.ConnectionString);
            Connections.Enqueue(new(target.ServerAlias, target, settings));
            return CreateDouble(target.ServerAlias, target, settings);
        }

        public Result<DbConnection> CreateCatalogConnection(string? serverAlias)
        {
            var physical = _production.CreateCatalogConnection(serverAlias);
            if (physical.IsFailure) return physical.Error;
            using var connection = physical.Value;
            string owner = _targets.ResolveServerAlias(serverAlias).Value;
            var settings = new SqlConnectionStringBuilder(connection.ConnectionString);
            Connections.Enqueue(new(owner, null, settings));
            return CreateDouble(owner, null, settings);
        }

        private FakeDbConnection CreateDouble(string owner, DatabaseTargetContext? target, SqlConnectionStringBuilder settings) =>
            new(connection => new FakeDbCommand(connection, new FakeDbCommandHandlers(
                ExecuteReaderAsync: (command, ct) => ReadAsync(owner, target, command, ct),
                ExecuteScalar: command => command.CommandText == "SELECT @@TRANCOUNT" ? 1
                    : throw new InvalidOperationException("Unexpected scalar command in composed fixture."),
                ExecuteNonQuery: command => IsKnownNonQuery(command.CommandText) ? 0
                    : throw new InvalidOperationException("Unexpected non-query command in composed fixture."))),
                new FakeDbConnectionOptions(Database: settings.InitialCatalog, DataSource: settings.DataSource,
                    ServerVersion: "16.0", BeginTransaction: (connection, _) => new FakeDbTransaction(connection)));

        private async Task<DbDataReader> ReadAsync(string owner, DatabaseTargetContext? target, FakeDbCommand command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string sql = command.CommandText;
            if (target is null && sql == "SELECT name FROM sys.databases WHERE state = 0")
                return new FakeDbDataReader(["name"], Catalogs[owner].Select(name => new object?[] { name }).ToArray());
            if (target is null) throw new InvalidOperationException("Unexpected catalog command in composed fixture.");
            if (sql.StartsWith("SELECT QUOTENAME(OBJECT_SCHEMA_NAME", StringComparison.Ordinal))
                return new FakeDbDataReader(["Value"], [["[dbo].[AnonymizationRules]"]]);
            if (sql.StartsWith("SELECT CASE WHEN COL_LENGTH", StringComparison.Ordinal))
                return new FakeDbDataReader(["Value"], [[true]]);
            if (sql == "SELECT [DatabasePattern], [SchemaPattern], [TablePattern], [ColumnPattern], [Anonymize] FROM [dbo].[AnonymizationRules] WHERE [IsActive] = 1")
            {
                RuleLoads.AddOrUpdate(target.QualifiedName, 1, (_, count) => count + 1);
                RuleLoadStarted.TrySetResult(target.QualifiedName);
                if (ColdRuleBarrier is { } barrier) await barrier.Arrive(target.QualifiedName).WaitAsync(TimeSpan.FromSeconds(10), ct);
                if (FailedRuleSources.Contains(target.QualifiedName)) throw new InvalidOperationException("Synthetic rule permission failure.");
                bool exempt = ExemptValues.GetValueOrDefault(target.QualifiedName);
                return new FakeDbDataReader(["DatabasePattern", "SchemaPattern", "TablePattern", "ColumnPattern", "Anonymize"],
                    [["%", "dbo", "T", "Value", !exempt]]);
            }
            if (sql == DataQuery || sql.StartsWith(DataQuery + " WHERE Secret = '", StringComparison.Ordinal))
            {
                Queries.Enqueue(new(target.QualifiedName, sql, ct));
                return new FakeDbDataReader(["Value", "Secret"], [[Values.GetValueOrDefault(target.QualifiedName, target.QualifiedName + "-value"), SharedSecret]],
                    new FakeSchemaTableOrigin("T", "Value", "dbo"));
            }
            throw new InvalidOperationException("Unexpected reader command in composed fixture.");
        }

        private static bool IsKnownNonQuery(string sql) => sql is DataQuery
            or "SET STATISTICS IO ON" or "SET STATISTICS IO OFF" or "SET STATISTICS TIME ON" or "SET STATISTICS TIME OFF"
            or "SET STATISTICS XML ON" or "SET STATISTICS XML OFF" or "SET NOEXEC ON" or "SET NOEXEC OFF"
            || sql.StartsWith("SET ROWCOUNT ", StringComparison.Ordinal) && int.TryParse(sql.AsSpan(13), out int count) && count >= 0;
    }
}
