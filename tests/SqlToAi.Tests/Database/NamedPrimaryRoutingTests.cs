using System.Security.Authentication;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Security;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class NamedPrimaryRoutingTests
{
    private static SqlToAiOptions CreateOptions() => new()
    {
        Servers = new(StringComparer.Ordinal)
        {
            ["TEST"] = new()
            {
                Server = "test-endpoint.invalid", IntegratedSecurity = true,
                ConnectTimeoutSeconds = 7, ExcludedDatabases = ["private*", "*/secret"]
            },
            ["prod"] = new()
            {
                Server = "prod-endpoint.invalid", IntegratedSecurity = false,
                UserId = "synthetic-user", Password = "synthetic-password", TrustServerCertificate = true,
                ConnectTimeoutSeconds = 19
            }
        },
        Databases = new()
        {
            ReadWrite = ["TEST/AppDb", "test/App/Db", "test/ 数据 /Db ", "test/ ", "test/privateDb", "prod/privateDb"],
            ReadOnly = ["prod/AppDb"]
        }
    };

    [Fact]
    public async Task SameNameTargets_KeepIndependentPolicyAndConnectionsInParallel()
    {
        var options = CreateOptions();
        var resolver = new DatabaseTargetResolver(options);
        var factory = new SqlConnectionFactory(Options.Create(options));
        var guard = new SecurityGuard(Options.Create(options));
        var access = new AccessLevelProvider(Options.Create(options));
        IQuerySafetyValidator validator = new QuerySafetyValidator(guard, access, new ReadOnlyGuard());
        var originalServers = options.Servers.ToArray();
        var originalGrants = options.Databases.ReadWrite.ToArray();

        await Task.WhenAll(Enumerable.Range(0, 200).Select(index => Task.Run(async () =>
        {
            bool test = index % 2 == 0;
            var target = resolver.Resolve(test ? "TEST/AppDb" : "PROD/appdb").Value;
            Assert.True(guard.IsTargetAllowed(target));
            Assert.Equal(test ? AccessLevel.ReadWrite : AccessLevel.ReadOnly,
                await access.GetTargetAccessLevelAsync(target, TestContext.Current.CancellationToken));
            var safety = await validator.ValidateTargetQuerySafetyAsync(target, "SELECT 1",
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(safety.IsSuccess);
            Assert.Same(target, safety.Value.Target);
            Assert.Equal(test, safety.Value.IsWriteAllowed);

            await using var connection = factory.CreateTargetConnection(target);
            var connectionString = new SqlConnectionStringBuilder(connection.ConnectionString);
            Assert.Equal(test ? "test-endpoint.invalid" : "prod-endpoint.invalid", connectionString.DataSource);
            Assert.Equal(target.DatabaseName, connectionString.InitialCatalog);
            Assert.Equal(test, connectionString.IntegratedSecurity);
            Assert.Equal(test ? "" : "synthetic-user", connectionString.UserID);
            Assert.Equal(test ? "" : "synthetic-password", connectionString.Password);
            Assert.Equal(!test, connectionString.TrustServerCertificate);
            Assert.Equal(test ? 7 : 19, connectionString.ConnectTimeout);
            Assert.Equal(SqlConnectionEncryptOption.Mandatory, connectionString.Encrypt);
        }, TestContext.Current.CancellationToken)));

        Assert.Equal(originalServers, options.Servers.ToArray());
        Assert.Equal(originalGrants, options.Databases.ReadWrite);
        Assert.Equal("test-endpoint.invalid", options.Servers["TEST"].Server);
        Assert.True(options.Servers["TEST"].IntegratedSecurity);
        Assert.False(options.Servers["TEST"].TrustServerCertificate);
        Assert.Equal(7, options.Servers["TEST"].ConnectTimeoutSeconds);
        Assert.Equal("prod-endpoint.invalid", options.Servers["prod"].Server);
        Assert.Equal("synthetic-password", options.Servers["prod"].Password);
    }

    [Theory]
    [InlineData("test/App/Db", "App/Db")]
    [InlineData("TEST/ 数据 /Db ", " 数据 /Db ")]
    [InlineData("test/ ", " ")]
    public async Task RawDatabaseCharacters_SurvivePolicyAndSqlClient(string input, string rawDatabase)
    {
        var options = CreateOptions();
        var target = new DatabaseTargetResolver(options).Resolve(input).Value;
        Assert.True(new SecurityGuard(Options.Create(options)).IsTargetAllowed(target));
        Assert.Equal(AccessLevel.ReadWrite, await new AccessLevelProvider(Options.Create(options))
            .GetTargetAccessLevelAsync(target, TestContext.Current.CancellationToken));
        await using var connection = new SqlConnectionFactory(Options.Create(options)).CreateTargetConnection(target);
        Assert.Equal(rawDatabase, new SqlConnectionStringBuilder(connection.ConnectionString).InitialCatalog);
    }

    [Theory]
    [InlineData("SchemaOnly", AccessLevel.SchemaOnly)]
    [InlineData("ReadOnlyAnonymized", AccessLevel.ReadOnlyAnonymized)]
    [InlineData("ReadOnly", AccessLevel.ReadOnly)]
    public async Task OverlappingQualifiedGrants_ChooseRestrictiveLevel(string restrictiveList, AccessLevel expected)
    {
        var options = CreateOptions();
        switch (restrictiveList)
        {
            case "SchemaOnly": options.Databases.SchemaOnly.Add("TeSt/aPPdB"); goto case "ReadOnlyAnonymized";
            case "ReadOnlyAnonymized": options.Databases.ReadOnlyAnonymized.Add("test/AppDb"); goto case "ReadOnly";
            case "ReadOnly": options.Databases.ReadOnly.Add("TEST/APPDB"); break;
        }
        var resolver = new DatabaseTargetResolver(options);
        var provider = new AccessLevelProvider(Options.Create(options));
        Assert.Equal(expected, await provider.GetTargetAccessLevelAsync(resolver.Resolve("test/AppDb").Value,
            TestContext.Current.CancellationToken));
        Assert.Equal(AccessLevel.ReadOnly, await provider.GetTargetAccessLevelAsync(resolver.Resolve("prod/AppDb").Value,
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("test/privateDb")]
    [InlineData("test/area/secret")]
    [InlineData("test/Denied")]
    [InlineData("prod/App/Db")]
    [InlineData("test/数据 /Db")]
    public async Task DeniedOrLocallyExcludedTarget_FailsBeforeConnection(string input)
    {
        var options = CreateOptions();
        options.Databases.ReadWrite.Add("test/area/secret");
        var target = new DatabaseTargetResolver(options).Resolve(input).Value;
        var guard = new SecurityGuard(Options.Create(options));
        var access = new AccessLevelProvider(Options.Create(options));
        Assert.False(guard.IsTargetAllowed(target));
        Assert.Equal(AccessLevel.None, await access.GetTargetAccessLevelAsync(target, TestContext.Current.CancellationToken));
        var safety = await new QuerySafetyValidator(guard, access, new ReadOnlyGuard())
            .ValidateTargetQuerySafetyAsync(target, "SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, safety.Error.Code);
        // Both guards have only options and target dependencies: no connection can be opened.
        var otherOwner = new DatabaseTargetResolver(options).Resolve("prod/privateDb").Value;
        Assert.True(guard.IsTargetAllowed(otherOwner));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AppDb")]
    [InlineData("test/")]
    [InlineData("bad.alias/AppDb")]
    [InlineData("unknown/AppDb")]
    public void InvalidTarget_FailsResolutionBeforeRouting(string? input)
    {
        var result = new DatabaseTargetResolver(CreateOptions()).Resolve(input);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public async Task NewEndpointAndUnqualifiedGrants_NeverGrantAccess()
    {
        var options = CreateOptions();
        options.Servers.Add("other", new() { Server = "test-endpoint.invalid", IntegratedSecurity = true });
        options.Databases.ReadWrite.Add("AppDb");
        var target = new DatabaseTargetResolver(options).Resolve("other/AppDb").Value;
        Assert.False(new SecurityGuard(Options.Create(options)).IsTargetAllowed(target));
        Assert.Equal(AccessLevel.None, await new AccessLevelProvider(Options.Create(options))
            .GetTargetAccessLevelAsync(target, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("SELECT 1", false, false, true, null)]
    [InlineData("DELETE FROM dbo.T", false, false, true, null)]
    [InlineData("USE OtherDb; SELECT 1", false, false, false, "SQL-AI-0104")]
    [InlineData("SELECT 1; SELECT 2", false, false, false, "SQL-AI-0101")]
    [InlineData("SELECT 1; SELECT 2", true, false, true, null)]
    [InlineData("SELECT 1", false, true, true, null)]
    public async Task TargetPipeline_PreservesQueryGuards(string query, bool batch, bool schemaOnly,
        bool succeeds, string? code)
    {
        var options = CreateOptions();
        if (schemaOnly) options.Databases.SchemaOnly.Add("test/AppDb");
        var target = new DatabaseTargetResolver(options).Resolve("test/AppDb").Value;
        var validator = new QuerySafetyValidator(new SecurityGuard(Options.Create(options)),
            new AccessLevelProvider(Options.Create(options)), new ReadOnlyGuard());
        var result = batch
            ? await validator.ValidateTargetBatchSafetyAsync(target, query, TestContext.Current.CancellationToken)
            : await validator.ValidateTargetQuerySafetyAsync(target, query, allowSchemaOnly: schemaOnly,
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(succeeds, result.IsSuccess);
        if (succeeds) Assert.Same(target, result.Value.Target);
        else Assert.Equal(code, result.Error.Code);
    }

    [Theory]
    [InlineData("SELECT 1", "SQL-AI-0107")]
    [InlineData("DELETE FROM dbo.T", "SQL-AI-0107")]
    public async Task SchemaOnlyTarget_BlocksDataAndMutations(string query, string code)
    {
        var options = CreateOptions();
        options.Databases.SchemaOnly.Add("test/AppDb");
        var target = new DatabaseTargetResolver(options).Resolve("test/AppDb").Value;
        var validator = new QuerySafetyValidator(new SecurityGuard(Options.Create(options)),
            new AccessLevelProvider(Options.Create(options)), new ReadOnlyGuard());
        Assert.Equal(code, (await validator.ValidateTargetQuerySafetyAsync(target, query,
            cancellationToken: TestContext.Current.CancellationToken)).Error.Code);
        if (query.StartsWith("DELETE", StringComparison.Ordinal))
        {
            Assert.Equal(code, (await validator.ValidateTargetQuerySafetyAsync(target, query, allowSchemaOnly: true,
                cancellationToken: TestContext.Current.CancellationToken)).Error.Code);
        }
    }

    [Fact]
    public void CatalogConnection_RequiresSelectedAliasAndNeverCreatesDatabaseTarget()
    {
        var options = CreateOptions();
        var factory = new SqlConnectionFactory(Options.Create(options));
        var result = factory.CreateCatalogConnection("PROD");
        Assert.True(result.IsSuccess);
        using var connection = result.Value;
        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal("prod-endpoint.invalid", builder.DataSource);
        Assert.Equal("", builder.InitialCatalog);
        Assert.Equal("synthetic-user", builder.UserID);
        Assert.True(builder.TrustServerCertificate);
        Assert.Equal(19, builder.ConnectTimeout);
        Assert.True(new DatabaseTargetResolver(options).Resolve("PROD").IsFailure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("test/AppDb")]
    [InlineData("bad.alias")]
    public void InvalidCatalogAlias_ReturnsInvalidParameters(string? alias)
    {
        var result = new SqlConnectionFactory(Options.Create(CreateOptions())).CreateCatalogConnection(alias);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
    }

    [Fact]
    public void ContextFromUnknownOwner_CannotFallBackToAnotherEndpoint()
    {
        var target = new DatabaseTargetResolver(new SqlToAiOptions
        {
            Servers = new() { ["other"] = new() }
        }).Resolve("other/AppDb").Value;
        var options = CreateOptions();
        Assert.False(new SecurityGuard(Options.Create(options)).IsTargetAllowed(target));
        Assert.Throws<InvalidOperationException>(() => new SqlConnectionFactory(Options.Create(options)).CreateTargetConnection(target));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingNamedSettings_CannotFallBackOrExposeCredentials(bool missingServer)
    {
        var options = CreateOptions();
        if (missingServer) options.Servers["prod"].Server = "";
        else options.Servers["prod"].UserId = "";
        var factory = new SqlConnectionFactory(Options.Create(options));
        var target = new DatabaseTargetResolver(options).Resolve("prod/AppDb").Value;
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateTargetConnection(target));
        Assert.Contains("SqlToAi:Servers:prod", exception.Message);
        Assert.DoesNotContain("synthetic-password", exception.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadOnlyTarget_BlocksMutatingQueriesAndBatches(bool batch)
    {
        var options = CreateOptions();
        var target = new DatabaseTargetResolver(options).Resolve("prod/AppDb").Value;
        var validator = new QuerySafetyValidator(new SecurityGuard(Options.Create(options)),
            new AccessLevelProvider(Options.Create(options)), new ReadOnlyGuard());
        var result = batch
            ? await validator.ValidateTargetBatchSafetyAsync(target, "SELECT 1; DELETE FROM dbo.T", TestContext.Current.CancellationToken)
            : await validator.ValidateTargetQuerySafetyAsync(target, "DELETE FROM dbo.T",
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, result.Error.Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PrimaryOpenErrors_IdentifyOwnerAndDiscardRawException(bool catalog)
    {
        const string secret = "Server=secret-host;User ID=secret-user;Password=secret-password;";
        Exception[] failures =
        [
            new AuthenticationException(secret, new InvalidOperationException(secret)),
            new TimeoutException(secret, new AuthenticationException(secret)),
            SqlExceptionFactory.Create(-2, "Timeout during TLS handshake. " + secret),
            new InvalidOperationException(secret)
        ];
        var target = new DatabaseTargetResolver(CreateOptions()).Resolve("PROD/AppDb").Value;
        foreach (var failure in failures)
        {
            using var connection = new FakeDbConnection(_ => throw new InvalidOperationException("No commands expected"),
                new FakeDbConnectionOptions(OpenFailure: failure));
            var exception = await Assert.ThrowsAsync<PrimaryConnectionException>(() => catalog
                ? PrimaryConnection.OpenCatalogAsync(connection, "PROD", TestContext.Current.CancellationToken)
                : PrimaryConnection.OpenTargetAsync(connection, target, TestContext.Current.CancellationToken));
            Assert.Equal(failure is TimeoutException || SqlToAiErrorMapper.IsTimeoutException(failure)
                ? SqlToAiError.TimeoutCode : SqlToAiError.InfrastructureErrorCode, exception.Error.Code);
            Assert.Contains("SqlToAi:Servers:prod", exception.Message);
            Assert.DoesNotContain("test", exception.Message);
            Assert.DoesNotContain("secret", exception.ToString());
            Assert.Null(exception.InnerException);
            Assert.Empty(exception.Data.Keys);
            Assert.Equal(exception.Error, SqlToAiErrorMapper.MapException(exception));
            Assert.Equal(failure is AuthenticationException, exception.IsTlsFailure);
            Assert.Equal(1, connection.OpenCount);
        }
    }

    [Fact]
    public async Task PrimaryOpenCancellation_PropagatesUnchanged()
    {
        var cancellation = new OperationCanceledException();
        using var connection = new FakeDbConnection(_ => throw new InvalidOperationException(),
            new FakeDbConnectionOptions(OpenFailure: cancellation));
        var target = new DatabaseTargetResolver(CreateOptions()).Resolve("test/AppDb").Value;
        Assert.Same(cancellation, await Assert.ThrowsAsync<OperationCanceledException>(() =>
            PrimaryConnection.OpenTargetAsync(connection, target, TestContext.Current.CancellationToken)));
    }
}
