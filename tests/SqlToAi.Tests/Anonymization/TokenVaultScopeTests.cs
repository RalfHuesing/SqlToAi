using SqlToAi.Anonymization;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Anonymization;

public sealed class TokenVaultScopeTests
{
    private static DatabaseTargetContext Target(string alias, string database) =>
        new(alias, database);

    [Fact]
    public void SamePlaintext_DifferentAliases_YieldsDistinctNonInterchangeableTokens()
    {
        var vault = new TokenVault();
        string testToken = vault.GetOrAddToken("secret-value", "§§§T", "§§§", Target("test", "AppDb"));
        string prodToken = vault.GetOrAddToken("secret-value", "§§§T", "§§§", Target("prod", "AppDb"));

        Assert.NotEqual(testToken, prodToken);
        Assert.True(vault.TryResolve(testToken, Target("test", "AppDb"), out string? testValue));
        Assert.Equal("secret-value", testValue);
        Assert.True(vault.TryResolve(prodToken, Target("prod", "AppDb"), out string? prodValue));
        Assert.Equal("secret-value", prodValue);
        Assert.False(vault.TryResolve(testToken, Target("prod", "AppDb"), out _));
        Assert.False(vault.TryResolve(prodToken, Target("test", "AppDb"), out _));
    }

    [Fact]
    public void OrdinallyDistinctDatabaseNames_AreDistinctScopes()
    {
        var vault = new TokenVault();
        var targets = new[]
        {
            Target("test", "AppDb"),
            Target("test", "appdb"),
            Target("test", "App/Db"),
            Target("test", "ÄppDb"),
        };
        var tokens = targets
            .Select(target => vault.GetOrAddToken("value", "§§§T", "§§§", target))
            .ToArray();

        Assert.Equal(4, tokens.Distinct().Count());
        for (int i = 0; i < targets.Length; i++)
        {
            for (int j = 0; j < targets.Length; j++)
            {
                bool resolved = vault.TryResolve(tokens[i], targets[j], out string? value);
                if (i == j)
                {
                    Assert.True(resolved);
                    Assert.Equal("value", value);
                }
                else
                {
                    Assert.False(resolved);
                }
            }
        }
    }

    [Fact]
    public void SameScopeReuse_ReturnsSameToken()
    {
        var vault = new TokenVault();
        var target = Target("test", "AppDb");

        string first = vault.GetOrAddToken("value", "§§§T", "§§§", target);
        string second = vault.GetOrAddToken("value", "§§§T", "§§§", target);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ExplicitStore_IsScopedAndNeverOverwritesAnotherOwnersToken()
    {
        var vault = new TokenVault();
        var test = Target("test", "AppDb");
        var prod = Target("prod", "AppDb");
        string issuedToken = vault.GetOrAddToken("test-owner-value", "§§§T", "§§§", test);

        vault.Store(issuedToken, "prod-adversarial-value", prod);
        vault.Store("§§§T1§§§", "prod-forged-value", prod);

        Assert.True(vault.TryResolve(issuedToken, test, out string? ownerValue));
        Assert.Equal("test-owner-value", ownerValue);
        Assert.True(vault.TryResolve(issuedToken, prod, out string? adversarialValue));
        Assert.Equal("prod-adversarial-value", adversarialValue);
        Assert.True(vault.TryResolve("§§§T1§§§", prod, out string? forgedValue));
        Assert.Equal("prod-forged-value", forgedValue);
        Assert.False(vault.TryResolve("§§§T1§§§", test, out _));
    }

    [Fact]
    public void TokenIds_AreUniqueAcrossScopes_ForDistinctPlaintext()
    {
        var vault = new TokenVault();
        string testToken = vault.GetOrAddToken("test-value", "§§§T", "§§§", Target("test", "AppDb"));
        string prodToken = vault.GetOrAddToken("prod-value", "§§§T", "§§§", Target("prod", "AppDb"));

        Assert.NotEqual(testToken, prodToken);
        Assert.False(vault.TryResolve(testToken, Target("prod", "AppDb"), out _));
    }

    [Fact]
    public async Task ParallelIssuanceAndResolution_NeverResolvesAcrossScopes()
    {
        var vault = new TokenVault();
        var aliases = new[] { "test", "prod", "dev" };
        var targets = aliases.Select(a => Target(a, "AppDb")).ToArray();

        var tasks = aliases
            .SelectMany((alias, index) => Enumerable.Range(0, 25).Select(_ => (Alias: alias, Index: index)))
            .Select(entry => Task.Run(() =>
            {
                string token = vault.GetOrAddToken($"value-{entry.Alias}", "§§§T", "§§§", targets[entry.Index]);
                Assert.True(vault.TryResolve(token, targets[entry.Index], out string? resolved));
                Assert.Equal($"value-{entry.Alias}", resolved);
                foreach (var other in targets.Where(t => !t.Equals(targets[entry.Index])))
                {
                    Assert.False(vault.TryResolve(token, other, out _));
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task ParallelSamePlaintextIssuance_YieldsOneReusableTokenPerScope()
    {
        var vault = new TokenVault();
        var targets = new[] { Target("test", "AppDb"), Target("prod", "AppDb") };

        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            var target = targets[i % targets.Length];
            string token = vault.GetOrAddToken("shared-value", "§§§T", "§§§", target);
            Assert.True(vault.TryResolve(token, target, out string? resolved));
            Assert.Equal("shared-value", resolved);
            return token;
        })).ToArray();

        string[] tokens = await Task.WhenAll(tasks);

        Assert.Equal(2, tokens.Distinct().Count());
        Assert.Single(tokens.Where(t => vault.TryResolve(t, targets[0], out _)).Distinct());
        Assert.Single(tokens.Where(t => vault.TryResolve(t, targets[1], out _)).Distinct());
    }
}
