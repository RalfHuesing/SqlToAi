using System.Text.Json;
using Microsoft.Data.SqlClient;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Database;

public sealed class ComposedProviderIsolationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ColdRuleLoadsOverlap_ThenAlternatingAndParallelRequestsUseTheirOwnCachesAndTokens()
    {
        var fixture = new ComposedTargetFixture();
        var barrier = new OwnerOverlapBarrier("alpha/SharedDb", "beta/SharedDb");
        fixture.Factory.ColdRuleBarrier = barrier;
        var cold = await Task.WhenAll(ExecuteAsync(fixture, "ALPHA/SharedDb"), ExecuteAsync(fixture, "BETA/SharedDb"));
        Assert.True(barrier.CrossOwnerOverlapReached);
        AssertOwnerOutput(cold[0], alpha: true);
        AssertOwnerOutput(cold[1], alpha: false);
        Assert.Equal(1, fixture.Factory.RuleLoads["alpha/SharedDb"]);
        Assert.Equal(1, fixture.Factory.RuleLoads["beta/SharedDb"]);
        Assert.NotEqual(cold[0].Secret, cold[1].Secret);

        for (int i = 0; i < 6; i++) AssertOwnerOutput(await ExecuteAsync(fixture, i % 2 == 0 ? "alpha/SharedDb" : "beta/SharedDb"), i % 2 == 0);
        var warm = await Task.WhenAll(Enumerable.Range(0, 24).Select(async i =>
            (Alpha: i % 2 == 0, Output: await ExecuteAsync(fixture, i % 2 == 0 ? "alpha/SharedDb" : "beta/SharedDb"))));
        foreach (var result in warm) AssertOwnerOutput(result.Output, result.Alpha);
        Assert.Equal(1, fixture.Factory.RuleLoads["alpha/SharedDb"]);
        Assert.Equal(1, fixture.Factory.RuleLoads["beta/SharedDb"]);

        var alpha = TestTargets.ResolveTarget(fixture.Configuration, "alpha/SharedDb");
        var beta = TestTargets.ResolveTarget(fixture.Configuration, "beta/SharedDb");
        Assert.True(fixture.Vault.TryResolve(cold[0].Secret, alpha, out string? original));
        Assert.Equal(ComposedTargetFixture.SharedSecret, original);
        Assert.False(fixture.Vault.TryResolve(cold[0].Secret, beta, out _));
        Assert.Equal(cold[0].Secret, (await ExecuteAsync(fixture, "alpha/SharedDb")).Secret);

        await ExecuteAsync(fixture, "alpha/SharedDb", ComposedTargetFixture.DataQuery + $" WHERE Secret = '{cold[0].Secret}'");
        var own = fixture.Factory.Queries.Last();
        Assert.Equal("alpha/SharedDb", own.Target);
        Assert.Contains("'" + ComposedTargetFixture.SharedSecret + "'", own.Sql, StringComparison.Ordinal);
        await ExecuteAsync(fixture, "beta/SharedDb", ComposedTargetFixture.DataQuery + $" WHERE Secret = '{cold[0].Secret}'");
        var foreign = fixture.Factory.Queries.Last();
        Assert.Contains("'" + cold[0].Secret + "'", foreign.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain(ComposedTargetFixture.SharedSecret, foreign.Sql, StringComparison.Ordinal);
        AssertConnectionsUseProductionSettings(fixture);
        Assert.All(fixture.Factory.Queries, command => Assert.Equal(Token, command.Token));
    }

    [Fact]
    public async Task SameAddressAndDatabase_KeepOwnersCredentialsRuleFailuresAndTokensSeparate()
    {
        var fixture = new ComposedTargetFixture(ComposedTargetFixture.CreateConfiguration(sameEndpoint: true));
        fixture.Factory.Values["alpha/SharedDb"] = "shared-value";
        fixture.Factory.Values["beta/SharedDb"] = "shared-value";
        fixture.Factory.FailedRuleSources.Add("beta/SharedDb");
        var alpha = await ExecuteAsync(fixture, "alpha/SharedDb");
        var beta = await ExecuteAsync(fixture, "beta/SharedDb");
        Assert.Equal("shared-value", alpha.Value);
        Assert.NotEqual("shared-value", beta.Value);
        Assert.NotEqual(alpha.Secret, beta.Secret);
        Assert.Equal(1, fixture.Factory.RuleLoads["alpha/SharedDb"]);
        Assert.Equal(1, fixture.Factory.RuleLoads["beta/SharedDb"]);
        Assert.Equal(beta.Value, (await ExecuteAsync(fixture, "beta/SharedDb")).Value);
        Assert.Equal("shared-value", (await ExecuteAsync(fixture, "alpha/SharedDb")).Value);
        Assert.Equal(1, fixture.Factory.RuleLoads["beta/SharedDb"]);
        AssertConnectionsUseProductionSettings(fixture);
        Assert.All(fixture.Factory.Connections, connection => Assert.Equal("shared.invalid", connection.Settings.DataSource));
        Assert.Equal(2, fixture.Factory.Connections.Select(connection => connection.Settings.UserID).Distinct().Count());
    }

    [Fact]
    public async Task FallbackRules_UseEachExactRawDatabaseAndDoNotShareCachedExemptions()
    {
        const string otherRaw = " Other/Db Å ";
        var configuration = ComposedTargetFixture.CreateConfiguration();
        configuration.Databases.ReadOnlyAnonymized.Add("alpha/" + otherRaw);
        var fixture = new ComposedTargetFixture(configuration);
        fixture.Factory.ExemptValues["alpha/" + otherRaw] = false;
        var first = await ExecuteAsync(fixture, "alpha/SharedDb");
        var second = await ExecuteAsync(fixture, "ALPHA/" + otherRaw);
        Assert.Equal("alpha/SharedDb-value", first.Value);
        Assert.NotEqual("alpha/" + otherRaw + "-value", second.Value);
        Assert.NotEqual(first.Secret, second.Secret);
        Assert.Equal(1, fixture.Factory.RuleLoads["alpha/SharedDb"]);
        Assert.Equal(1, fixture.Factory.RuleLoads["alpha/" + otherRaw]);
        await ExecuteAsync(fixture, "alpha/SharedDb");
        await ExecuteAsync(fixture, "alpha/" + otherRaw);
        Assert.Equal(2, fixture.Factory.RuleLoads.Values.Sum());
        AssertConnectionsUseProductionSettings(fixture);
        Assert.Equal(3, fixture.Factory.Connections.Count(connection => connection.Settings.InitialCatalog == otherRaw));
    }

    [Fact]
    public async Task SameAddress_ExcludedOwnerDoesNotBlockOtherOwnersGrant()
    {
        var configuration = ComposedTargetFixture.CreateConfiguration(sameEndpoint: true);
        configuration.Servers["ALPHA"].ExcludedDatabases.Add("SharedDb");
        var fixture = new ComposedTargetFixture(configuration);
        var denied = await fixture.Query.ExecuteQueryAsync("alpha/SharedDb", ComposedTargetFixture.DataQuery, null, Token);
        Assert.True(denied.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, denied.Error.Code);
        Assert.Empty(fixture.Factory.Connections);
        AssertOwnerOutput(await ExecuteAsync(fixture, "beta/SharedDb"), alpha: false);
        Assert.All(fixture.Factory.Connections, connection => Assert.Equal("beta", connection.Owner));
    }

    [Theory]
    [InlineData("alpha/NotGranted", SqlToAiError.SafetyCheckFailedCode)]
    [InlineData("alpha/ExcludedDb", SqlToAiError.SafetyCheckFailedCode)]
    [InlineData("alpha/SchemaDb", SqlToAiError.WriteOperationBlockedCode)]
    public async Task RealPolicy_RejectsBeforeConnectionOrRuleLoad(string target, string code)
    {
        var configuration = ComposedTargetFixture.CreateConfiguration(sameEndpoint: true);
        configuration.Databases.ReadOnlyAnonymized.Add("alpha/ExcludedDb");
        configuration.Servers["ALPHA"].ExcludedDatabases.Add("Excluded*");
        configuration.Databases.SchemaOnly.Add("alpha/SchemaDb");
        var fixture = new ComposedTargetFixture(configuration);
        var result = await fixture.Query.ExecuteQueryAsync(target, ComposedTargetFixture.DataQuery, null, Token);
        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Empty(fixture.Factory.Connections);
        Assert.Empty(fixture.Factory.RuleLoads);
    }

    [Fact]
    public async Task CancelledColdRuleLoad_PreservesTokenAndDoesNotCacheFallback()
    {
        var fixture = new ComposedTargetFixture();
        fixture.Factory.ColdRuleBarrier = new OwnerOverlapBarrier("alpha/SharedDb", "beta/SharedDb");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var query = fixture.Query.ExecuteQueryAsync("alpha/SharedDb", ComposedTargetFixture.DataQuery, null, cancellation.Token);
        Assert.Equal("alpha/SharedDb", await fixture.Factory.RuleLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Token));
        await cancellation.CancelAsync();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        fixture.Factory.ColdRuleBarrier = null;
        Assert.Equal("alpha/SharedDb-value", (await ExecuteAsync(fixture, "alpha/SharedDb")).Value);
        Assert.Equal(2, fixture.Factory.RuleLoads["alpha/SharedDb"]);
    }

    private static void AssertOwnerOutput((string Value, string Secret) output, bool alpha)
    {
        if (alpha) Assert.Equal("alpha/SharedDb-value", output.Value);
        else Assert.StartsWith("§§§", output.Value, StringComparison.Ordinal);
        Assert.StartsWith("§§§", output.Secret, StringComparison.Ordinal);
    }

    private static async Task<(string Value, string Secret)> ExecuteAsync(ComposedTargetFixture fixture, string target, string? sql = null)
    {
        var result = await fixture.Query.ExecuteQueryAsync(target, sql ?? ComposedTargetFixture.DataQuery, null, Token);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        using var data = JsonDocument.Parse(result.Value.Data.Trim());
        return (data.RootElement.GetProperty("Value").GetString()!, data.RootElement.GetProperty("Secret").GetString()!);
    }

    private static void AssertConnectionsUseProductionSettings(ComposedTargetFixture fixture)
    {
        Assert.NotEmpty(fixture.Factory.Connections);
        foreach (var observation in fixture.Factory.Connections)
        {
            var expected = fixture.Configuration.Servers[observation.Owner == "alpha" ? "ALPHA" : "beta"];
            var actual = observation.Settings;
            Assert.Equal(expected.Server, actual.DataSource);
            Assert.Equal(observation.Target?.DatabaseName ?? "", actual.InitialCatalog);
            Assert.Equal(expected.IntegratedSecurity, actual.IntegratedSecurity);
            Assert.Equal(expected.IntegratedSecurity ? "" : expected.UserId, actual.UserID);
            Assert.Equal(expected.IntegratedSecurity ? "" : expected.Password, actual.Password);
            Assert.Equal(expected.TrustServerCertificate, actual.TrustServerCertificate);
            Assert.Equal(expected.ConnectTimeoutSeconds, actual.ConnectTimeout);
            Assert.Equal(SqlConnectionEncryptOption.Mandatory, actual.Encrypt);
        }
    }
}
