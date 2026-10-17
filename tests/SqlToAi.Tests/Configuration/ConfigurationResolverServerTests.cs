using SqlToAi.Configuration;

namespace SqlToAi.Tests.Configuration;

/// <summary>
/// Verifies that environment expansion and provider SQL-file resolution cover the per-server
/// settings while the user-root path base and existing expansion semantics are preserved.
/// </summary>
public sealed class ConfigurationResolverServerTests
{
    private static string ResolveInTempRoot(Action<SqlToAiOptions> configure, Action<string> onRoot)
    {
        string root = Directory.CreateTempSubdirectory("sqltoai-config-resolver-").FullName;
        onRoot(root);
        var options = new SqlToAiOptions();
        configure(options);
        ConfigurationResolver.Resolve(options, root);
        return root;
    }

    [Fact]
    public void Resolve_ExpandsEnvironmentVariablesInPerServerSettings()
    {
        Environment.SetEnvironmentVariable("SQLAI_TEST_SERVER_HOST", "srv-host");
        Environment.SetEnvironmentVariable("SQLAI_TEST_USER", "svc-account");
        try
        {
            var options = new SqlToAiOptions();
            var server = new NamedServerOptions
            {
                Server = "host-%SQLAI_TEST_SERVER_HOST%",
                UserId = "%SQLAI_TEST_USER%",
                Password = "%SQLAI_TEST_USER%-secret"
            };
            server.MetadataProvider.Server = "%SQLAI_TEST_SERVER_HOST%";
            server.MetadataProvider.Database = "%SQLAI_TEST_USER%-db";
            server.AnonymizationRules.Server = "%SQLAI_TEST_SERVER_HOST%";
            server.AnonymizationRules.Database = "%SQLAI_TEST_USER%-rules";
            options.Servers["test"] = server;

            ConfigurationResolver.Resolve(options, Path.GetTempPath());

            Assert.Equal("host-srv-host", server.Server);
            Assert.Equal("svc-account", server.UserId);
            Assert.Equal("svc-account-secret", server.Password);
            Assert.Equal("srv-host", server.MetadataProvider.Server);
            Assert.Equal("svc-account-db", server.MetadataProvider.Database);
            Assert.Equal("srv-host", server.AnonymizationRules.Server);
            Assert.Equal("svc-account-rules", server.AnonymizationRules.Database);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SQLAI_TEST_SERVER_HOST", null);
            Environment.SetEnvironmentVariable("SQLAI_TEST_USER", null);
        }
    }

    [Fact]
    public void Resolve_LoadsPerServerMetadataQueriesFromSqlFilesRelativeToUserRoot()
    {
        const string tableSql = "SELECT 'owner table' AS Description";
        const string columnSql = "SELECT 'owner column' AS Description";
        NamedServerOptions? resolvedServer = null;

        string root = ResolveInTempRoot(
            options =>
            {
                var server = new NamedServerOptions { Server = "test-host" };
                server.MetadataProvider.TableMetadataQuery = "table-doc.sql";
                server.MetadataProvider.ColumnMetadataQuery = "column-doc.sql";
                options.Servers["test"] = server;
                resolvedServer = server;
            },
            onRoot: r =>
            {
                File.WriteAllText(Path.Combine(r, "table-doc.sql"), tableSql);
                File.WriteAllText(Path.Combine(r, "column-doc.sql"), columnSql);
            });

        try
        {
            Assert.NotNull(resolvedServer);
            Assert.Equal(tableSql, resolvedServer.MetadataProvider.TableMetadataQuery);
            Assert.Equal(columnSql, resolvedServer.MetadataProvider.ColumnMetadataQuery);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_MissingPerServerSqlFile_ThrowsFileNotFound()
    {
        string root = Directory.CreateTempSubdirectory("sqltoai-config-resolver-").FullName;
        try
        {
            var options = new SqlToAiOptions();
            var server = new NamedServerOptions { Server = "test-host" };
            server.MetadataProvider.TableMetadataQuery = "missing-doc.sql";
            options.Servers["test"] = server;

            Assert.Throws<FileNotFoundException>(() => ConfigurationResolver.Resolve(options, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
