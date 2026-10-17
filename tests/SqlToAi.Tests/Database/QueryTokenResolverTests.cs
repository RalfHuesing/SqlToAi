using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

public sealed class QueryTokenResolverTests
{
    private static readonly DatabaseTargetContext Target = new("test", "DemoDB");
    private static AnonymizationColumnContext Col(string name) => new(null, name, null);

    private static QueryTokenResolver BuildResolver(ITokenVault vault, bool enabled = true)
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Tokenization.Enabled = enabled;
        return new QueryTokenResolver(vault, Options.Create(options));
    }

    [Fact]
    public void ResolveTokens_ShouldReturnQueryUnchanged_WhenDisabled()
    {
        var vault = new TokenVault();
        vault.Store("§§§tok§§§", "RealValue", Target);
        var resolver = BuildResolver(vault, enabled: false);

        string result = resolver.ResolveTokens("SELECT * FROM T WHERE A = '§§§tok§§§'", Target);

        Assert.Contains("§§§tok§§§", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveTokens_ShouldSubstituteRealValue_ForEqualityComparison()
    {
        var vault = new TokenVault();
        vault.Store("§§§tok§§§", "DE89370400440532013000", Target);
        var resolver = BuildResolver(vault);

        string result = resolver.ResolveTokens("SELECT * FROM Accounts WHERE IBAN = '§§§tok§§§'", Target);

        Assert.Equal("SELECT * FROM Accounts WHERE IBAN = 'DE89370400440532013000'", result);
    }

    [Fact]
    public void ResolveTokens_ShouldSubstituteRealValue_InsideLikeWildcardPattern()
    {
        var vault = new TokenVault();
        vault.Store("§§§tok§§§", "37040044", Target);
        var resolver = BuildResolver(vault);

        string result = resolver.ResolveTokens("SELECT * FROM Accounts WHERE IBAN LIKE '%§§§tok§§§%'", Target);

        Assert.Equal("SELECT * FROM Accounts WHERE IBAN LIKE '%37040044%'", result);
    }

    [Fact]
    public void ResolveTokens_ShouldSubstituteEachToken_InAnInList()
    {
        var vault = new TokenVault();
        vault.Store("§§§a§§§", "111", Target);
        vault.Store("§§§b§§§", "222", Target);
        var resolver = BuildResolver(vault);

        string result = resolver.ResolveTokens("SELECT * FROM T WHERE Id IN ('§§§a§§§', '§§§b§§§')", Target);

        Assert.Equal("SELECT * FROM T WHERE Id IN ('111', '222')", result);
    }

    [Fact]
    public void ResolveTokens_ShouldSubstituteRealValue_ForRangeComparison()
    {
        var vault = new TokenVault();
        vault.Store("§§§tok§§§", "500", Target);
        var resolver = BuildResolver(vault);

        string result = resolver.ResolveTokens("SELECT * FROM T WHERE Amount >= '§§§tok§§§'", Target);

        Assert.Equal("SELECT * FROM T WHERE Amount >= '500'", result);
    }

    [Fact]
    public void ResolveTokens_ShouldLeaveUnknownToken_Untouched()
    {
        var vault = new TokenVault(); // nothing stored
        var resolver = BuildResolver(vault);

        string result = resolver.ResolveTokens("SELECT * FROM T WHERE A = '§§§forged§§§'", Target);

        Assert.Equal("SELECT * FROM T WHERE A = '§§§forged§§§'", result);
    }

    [Fact]
    public void ResolveTokens_ShouldEscapeSingleQuotes_InSubstitutedValue()
    {
        var vault = new TokenVault();
        vault.Store("§§§tok§§§", "O'Brien", Target);
        var resolver = BuildResolver(vault);

        string result = resolver.ResolveTokens("SELECT * FROM T WHERE Name = '§§§tok§§§'", Target);

        Assert.Equal("SELECT * FROM T WHERE Name = 'O''Brien'", result);
    }

    [Fact]
    public void ResolveTokens_ShouldNotSubstitute_InsideComments()
    {
        var vault = new TokenVault();
        vault.Store("§§§tok§§§", "RealValue", Target);
        var resolver = BuildResolver(vault);

        string result = resolver.ResolveTokens("SELECT 1 -- '§§§tok§§§'\nWHERE 1=1", Target);

        Assert.Contains("§§§tok§§§", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveTokens_ShouldNotAffectQueriesWithoutTokens()
    {
        var vault = new TokenVault();
        var resolver = BuildResolver(vault);

        const string query = "SELECT * FROM Customers WHERE Id = 1";
        string result = resolver.ResolveTokens(query, Target);

        Assert.Equal(query, result);
    }

    [Fact]
    public void ResolveTokens_ShouldUseConfiguredPrefixAndSuffix()
    {
        var vault = new TokenVault();
        vault.Store("<<tok>>", "RealValue", Target);
        var options = new SqlToAiOptions();
        options.Anonymizer.Tokenization.Enabled = true;
        options.Anonymizer.Tokenization.Prefix = "<<";
        options.Anonymizer.Tokenization.Suffix = ">>";
        var resolver = new QueryTokenResolver(vault, Options.Create(options));

        string result = resolver.ResolveTokens("SELECT * FROM T WHERE A = '<<tok>>'", Target);

        Assert.Equal("SELECT * FROM T WHERE A = 'RealValue'", result);
    }

    [Fact]
    public void ResolveTokens_ShouldRoundTrip_TokenProducedByAnonymizerTokenize()
    {
        var vault = new TokenVault();
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.Tokenization.Enabled = true;
        var anonymizer = new Anonymizer(Options.Create(options), vault);
        var resolver = new QueryTokenResolver(vault, Options.Create(options));

        // 1. Egress: the AI receives a token instead of the real IBAN.
        string token = anonymizer.Tokenize("DE89370400440532013000", Col("IBAN"), Target);

        // 2. Ingress: the AI reuses that exact token in a later query.
        string result = resolver.ResolveTokens($"SELECT * FROM Accounts WHERE IBAN = '{token}'", Target);

        Assert.Equal("SELECT * FROM Accounts WHERE IBAN = 'DE89370400440532013000'", result);
    }
}
