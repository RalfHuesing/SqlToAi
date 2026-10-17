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

    [Theory]
    [InlineData("SELECT 1 AS [a]]'§§§tok§§§']")]
    [InlineData("SELECT 1 AS \"'§§§tok§§§'\"")]
    [InlineData("SELECT 1 AS \"a\"\"'§§§tok§§§'\"")]
    [InlineData("SELECT 1 /* outer /* '§§§tok§§§' */ still outer */")]
    [InlineData("SELECT 1 /* outer /* inner */ '§§§tok§§§' */")]
    public void ResolveTokens_ShouldPreserveTokensInsideEscapedIdentifiersAndNestedComments(string query)
    {
        var vault = new TokenVault();
        vault.Store("§§§tok§§§", "PLAINTEXT", Target);
        var resolver = BuildResolver(vault);

        Assert.Equal(query, resolver.ResolveTokens(query, Target));
    }

    [Theory]
    [InlineData("SELECT 1 AS [unterminated '§§§tok§§§'")]
    [InlineData("SELECT 1 AS \"unterminated '§§§tok§§§'")]
    [InlineData("SELECT 1 /* outer /* '§§§tok§§§' */")]
    [InlineData("SELECT '§§§tok§§§' AS Value /* unterminated")]
    [InlineData("SELECT '§§§tok§§§' AS Value, 'unterminated")]
    [InlineData("SELECT '§§§tok§§§' AS 'unterminated")]
    [InlineData("SELECT '§§§tok§§§' AS Value FROM")]
    public void ResolveTokens_ShouldLeaveTokensInMalformedTailsUnchanged(string query)
    {
        var vault = new TokenVault();
        vault.Store("§§§tok§§§", "PLAINTEXT", Target);
        var resolver = BuildResolver(vault);

        Assert.Equal(query, resolver.ResolveTokens(query, Target));
    }

    [Theory]
    [InlineData("1 AS '§§§tok§§§'", "1 AS '§§§tok§§§'")]
    [InlineData("1 AS [a]]'§§§tok§§§']", "1 AS [a]]'§§§tok§§§']")]
    [InlineData("1 AS \"a\"\"'§§§tok§§§'\"", "1 AS \"a\"\"'§§§tok§§§'\"")]
    [InlineData("1 '§§§tok§§§'", "1 '§§§tok§§§'")]
    [InlineData("'§§§tok§§§' = 1", "'§§§tok§§§' = 1")]
    [InlineData("N'§§§tok§§§' = N'§§§tok§§§'", "N'§§§tok§§§' = N'O''Brien'")]
    [InlineData("'§§§tok§§§' AS 'a''§§§tok§§§'", "'O''Brien' AS 'a''§§§tok§§§'")]
    [InlineData("'§§§tok§§§'", "'O''Brien'")]
    public void ResolveTokens_ShouldPreserveAliasesWhileResolvingValueExpressions(string projection, string expectedProjection)
    {
        var vault = new TokenVault();
        vault.Store("§§§tok§§§", "O'Brien", Target);
        var resolver = BuildResolver(vault);

        string query = $"SELECT {projection} FROM T WHERE Name = '§§§tok§§§'";

        Assert.Equal($"SELECT {expectedProjection} FROM T WHERE Name = 'O''Brien'", resolver.ResolveTokens(query, Target));
    }

    [Fact]
    public void ResolveTokens_ShouldResolveDeclareAndNestedExpressionLiteralsInSourceOrder()
    {
        var vault = new TokenVault();
        vault.Store("§§§tok§§§", "O'Brien", Target);
        var resolver = BuildResolver(vault);
        const string query = "DECLARE @name nvarchar(100) = N'§§§tok§§§'; SELECT CASE WHEN Name = @name THEN N'§§§tok§§§' ELSE '§§§unknown§§§' END AS '§§§tok§§§' FROM T WHERE Name IN ('§§§tok§§§', '§§§unknown§§§') ORDER BY CASE WHEN Name LIKE '%§§§tok§§§%' THEN 0 ELSE 1 END";
        const string expected = "DECLARE @name nvarchar(100) = N'O''Brien'; SELECT CASE WHEN Name = @name THEN N'O''Brien' ELSE '§§§unknown§§§' END AS '§§§tok§§§' FROM T WHERE Name IN ('O''Brien', '§§§unknown§§§') ORDER BY CASE WHEN Name LIKE '%O''Brien%' THEN 0 ELSE 1 END";

        Assert.Equal(expected, resolver.ResolveTokens(query, Target));
    }

    [Fact]
    public void ResolveTokens_ShouldResumeValueSubstitutionAfterNestedComments()
    {
        var vault = new TokenVault();
        vault.Store("§§§tok§§§", "PLAINTEXT", Target);
        var resolver = BuildResolver(vault);
        const string query = "SELECT 1 /* outer /* inner */ '§§§tok§§§' */ WHERE Name = '§§§tok§§§'";

        Assert.Equal("SELECT 1 /* outer /* inner */ '§§§tok§§§' */ WHERE Name = 'PLAINTEXT'", resolver.ResolveTokens(query, Target));
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
