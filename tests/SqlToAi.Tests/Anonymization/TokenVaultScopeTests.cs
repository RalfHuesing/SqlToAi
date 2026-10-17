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
    public void ExplicitStore_ShouldRejectBindingIssuedToken_ToAnotherScope()
    {
        var vault = new TokenVault();
        var test = Target("test", "AppDb");
        var prod = Target("prod", "AppDb");
        string issuedToken = vault.GetOrAddToken("test-owner-value", "§§§T", "§§§", test);

        Assert.Throws<InvalidOperationException>(() => vault.Store(issuedToken, "prod-adversarial-value", prod));

        Assert.True(vault.TryResolve(issuedToken, test, out string? ownerValue));
        Assert.Equal("test-owner-value", ownerValue);
        Assert.False(vault.TryResolve(issuedToken, prod, out _));
    }

    [Fact]
    public void ExplicitStore_ShouldReserveFutureMintedTokenText_AcrossScopes()
    {
        var vault = new TokenVault();
        var test = Target("test", "AppDb");
        var prod = Target("prod", "AppDb");

        // Minted token text with prefix "§§§T" is e.g. "§§§TT1§§§" — the next mint would use
        // "§§§TT2§§§". Reserving exactly that text in prod must force the next mint to skip it.
        string first = vault.GetOrAddToken("test-value-1", "§§§T", "§§§", test);
        Assert.Equal("§§§TT1§§§", first);
        vault.Store("§§§TT2§§§", "prod-reserved-value", prod);

        string second = vault.GetOrAddToken("test-value-2", "§§§T", "§§§", test);
        Assert.Equal("§§§TT3§§§", second);

        Assert.True(vault.TryResolve("§§§TT2§§§", prod, out string? reservedValue));
        Assert.Equal("prod-reserved-value", reservedValue);
        Assert.False(vault.TryResolve("§§§TT2§§§", test, out _));
        Assert.True(vault.TryResolve(second, test, out string? secondValue));
        Assert.Equal("test-value-2", secondValue);
        Assert.False(vault.TryResolve(second, prod, out _));
    }

    [Fact]
    public void ExplicitStore_ShouldAllowOverwrite_WithinOwningScope()
    {
        var vault = new TokenVault();
        var target = Target("test", "AppDb");
        vault.Store("§§§TT1§§§", "First", target);
        vault.Store("§§§TT1§§§", "Second", target);

        Assert.True(vault.TryResolve("§§§TT1§§§", target, out string? value));
        Assert.Equal("Second", value);
    }

    [Fact]
    public async Task CompetingStores_SameTokenText_DifferentScopes_OnlyOneOwnerWins()
    {
        var vault = new TokenVault();
        var scopes = new[]
        {
            Target("test", "AppDb"),
            Target("prod", "AppDb"),
            Target("dev", "AppDb"),
        };

        var tasks = scopes.Select((scope, index) => Task.Run(() =>
        {
            try
            {
                vault.Store("§§§Tcontention§§§", $"value-{index}", scope);
                return (Scope: scope, Won: true, Threw: false);
            }
            catch (InvalidOperationException)
            {
                return (Scope: scope, Won: false, Threw: true);
            }
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.Won));
        Assert.Equal(scopes.Length - 1, results.Count(r => r.Threw));
        foreach (var result in results)
        {
            if (result.Won)
            {
                Assert.True(vault.TryResolve("§§§Tcontention§§§", result.Scope, out string? value));
                Assert.Equal($"value-{Array.IndexOf(scopes, result.Scope)}", value);
            }
            else
            {
                Assert.False(vault.TryResolve("§§§Tcontention§§§", result.Scope, out _));
            }
        }
    }

    [Fact]
    public async Task ConcurrentIssuanceAndStore_NeverCreateTwoOwnersForOneTokenText()
    {
        var vault = new TokenVault();
        var test = Target("test", "AppDb");
        var prod = Target("prod", "AppDb");
        const int storeCount = 100;

        var storeTasks = Enumerable.Range(1, storeCount).Select(i => Task.Run(() =>
            vault.Store($"§§§TT{i}§§§", $"stored-{i}", prod))).ToArray();
        var mintTasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            vault.GetOrAddToken($"minted-{Guid.NewGuid():N}", "§§§T", "§§§", test))).ToArray();

        await Task.WhenAll(storeTasks.Concat(mintTasks));

        string[] minted = await Task.WhenAll(mintTasks);
        Assert.Empty(minted.Intersect(Enumerable.Range(1, storeCount).Select(i => $"§§§TT{i}§§§")));
        foreach (string token in minted)
        {
            Assert.True(vault.TryResolve(token, test, out _));
            Assert.False(vault.TryResolve(token, prod, out _));
        }
        for (int i = 1; i <= storeCount; i++)
        {
            Assert.True(vault.TryResolve($"§§§TT{i}§§§", prod, out _));
            Assert.False(vault.TryResolve($"§§§TT{i}§§§", test, out _));
        }
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
