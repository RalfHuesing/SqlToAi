using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

public sealed class QueryTokenResolverScopeTests
{
    private static QueryTokenResolver BuildResolver(ITokenVault vault)
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Tokenization.Enabled = true;
        return new QueryTokenResolver(vault, Options.Create(options));
    }

    [Fact]
    public void ResolveTokens_ShouldSubstituteToken_WithinItsOwnScope()
    {
        var vault = new TokenVault();
        var target = new DatabaseTargetContext("test", "AppDb");
        string token = vault.GetOrAddToken("DE89370400440532013000", "§§§T", "§§§", target);
        var resolver = BuildResolver(vault);

        string result = resolver.ResolveTokens($"SELECT * FROM Accounts WHERE IBAN = '{token}'", target);

        Assert.Equal("SELECT * FROM Accounts WHERE IBAN = 'DE89370400440532013000'", result);
    }

    [Fact]
    public void ResolveTokens_ShouldLeaveForeignTokenLiteral_InAnotherScope()
    {
        var vault = new TokenVault();
        var test = new DatabaseTargetContext("test", "AppDb");
        var prod = new DatabaseTargetContext("prod", "AppDb");
        string token = vault.GetOrAddToken("DE89370400440532013000", "§§§T", "§§§", test);
        var resolver = BuildResolver(vault);

        string result = resolver.ResolveTokens($"SELECT * FROM Accounts WHERE IBAN = '{token}'", prod);

        Assert.Equal($"SELECT * FROM Accounts WHERE IBAN = '{token}'", result);
    }

    [Fact]
    public void ResolveTokens_ShouldLeaveUnknownTokenLiteral_EvenWhenItMatchesRealSqlText()
    {
        var vault = new TokenVault();
        var target = new DatabaseTargetContext("test", "AppDb");
        var resolver = BuildResolver(vault);

        // The string looks like a token, but was never issued — it is ordinary literal text and
        // must survive unchanged; matching nothing is the fail-safe outcome, not an error.
        const string query = "SELECT * FROM T WHERE Note = '§§§T7§§§'";

        Assert.Equal(query, resolver.ResolveTokens(query, target));
    }

    [Fact]
    public void ResolveTokens_ShouldEscapeQuotes_AndLeaveCommentsAndIdentifiersUntouched_InScope()
    {
        var vault = new TokenVault();
        var target = new DatabaseTargetContext("test", "AppDb");
        string nameToken = vault.GetOrAddToken("O'Brien", "§§§T", "§§§", target);
        string commentToken = vault.GetOrAddToken("CommentValue", "§§§T", "§§§", target);
        var resolver = BuildResolver(vault);

        string result = resolver.ResolveTokens(
            $"SELECT [§§§T1§§§] -- '§§§{commentToken}§§§'\nFROM dbo.T WHERE Name = '{nameToken}'", target);

        Assert.Contains("O''Brien", result, StringComparison.Ordinal);
        Assert.Contains($"§§§{commentToken}§§§", result, StringComparison.Ordinal);
        Assert.Contains("[§§§T1§§§]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveTokens_ShouldLeaveIssuedTokenLiteral_AfterRejectedForeignStore()
    {
        var vault = new TokenVault();
        var test = new DatabaseTargetContext("test", "AppDb");
        var prod = new DatabaseTargetContext("prod", "AppDb");
        string token = vault.GetOrAddToken("DE89370400440532013000", "§§§T", "§§§", test);
        Assert.Throws<InvalidOperationException>(() => vault.Store(token, "attacker-value", prod));
        var resolver = BuildResolver(vault);

        string result = resolver.ResolveTokens($"SELECT * FROM Accounts WHERE IBAN = '{token}'", prod);

        Assert.Equal($"SELECT * FROM Accounts WHERE IBAN = '{token}'", result);
    }

    [Fact]
    public async Task ResolveTokens_ShouldRouteEachParallelQuery_ToItsOwnScope()
    {
        var vault = new TokenVault();
        var resolver = BuildResolver(vault);
        var targets = new[]
        {
            new DatabaseTargetContext("test", "AppDb"),
            new DatabaseTargetContext("prod", "AppDb"),
            new DatabaseTargetContext("dev", "AppDb"),
        };
        var tokens = targets.ToDictionary(
            t => t.QualifiedName,
            t => vault.GetOrAddToken("secret", "§§§T", "§§§", t));

        var tasks = targets.SelectMany(t => Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            resolver.ResolveTokens($"SELECT * FROM T WHERE A = '{tokens[t.QualifiedName]}'", t)))).ToArray();

        string[] results = await Task.WhenAll(tasks);
        int index = 0;
        foreach (var target in targets)
        {
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal("SELECT * FROM T WHERE A = 'secret'", results[index + i]);
            }
            index += 20;
        }
    }
}
