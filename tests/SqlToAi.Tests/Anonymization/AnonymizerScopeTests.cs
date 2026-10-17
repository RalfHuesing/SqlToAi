using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Anonymization;

public sealed class AnonymizerScopeTests
{
    private static Anonymizer BuildAnonymizer(ITokenVault vault, bool enabled, bool tokenizationUsable = true)
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = enabled;
        options.Anonymizer.DefaultMode = AnonymizationMode.Hash;
        if (tokenizationUsable)
        {
            options.Anonymizer.Tokenization.Enabled = true;
            options.Anonymizer.Tokenization.Prefix = "§§§T";
            options.Anonymizer.Tokenization.Suffix = "§§§";
        }
        return new Anonymizer(Options.Create(options), vault);
    }

    private static void AssertLooksHashed(string value)
    {
        Assert.Matches("^[0-9a-f]{64}$", value);
    }

    [Fact]
    public void Tokenize_ShouldScopeTokens_ByTarget()
    {
        var vault = new TokenVault();
        var anonymizer = BuildAnonymizer(vault, enabled: true);
        var context = new AnonymizationColumnContext("D", "Name", "dbo");
        var test = new DatabaseTargetContext("test", "AppDb");
        var prod = new DatabaseTargetContext("prod", "AppDb");

        string testToken = anonymizer.Tokenize("secret-value", context, test);
        string prodToken = anonymizer.Tokenize("secret-value", context, prod);

        Assert.NotEqual(testToken, prodToken);
        Assert.True(vault.TryResolve(testToken, test, out _));
        Assert.True(vault.TryResolve(prodToken, prod, out _));
        Assert.False(vault.TryResolve(testToken, prod, out _));
        Assert.False(vault.TryResolve(prodToken, test, out _));
    }

    [Fact]
    public void Tokenize_ShouldReuseSameToken_WithinOneScope()
    {
        var vault = new TokenVault();
        var anonymizer = BuildAnonymizer(vault, enabled: true);
        var context = new AnonymizationColumnContext("D", "Name", "dbo");
        var target = new DatabaseTargetContext("test", "AppDb");

        Assert.Equal(
            anonymizer.Tokenize("value", context, target),
            anonymizer.Tokenize("value", context, target));
    }

    [Fact]
    public void Tokenize_ShouldReturnOriginalValue_WhenMasterSwitchDisabled()
    {
        var vault = new TokenVault();
        var anonymizer = BuildAnonymizer(vault, enabled: false);
        var context = new AnonymizationColumnContext("D", "Name", "dbo");
        var target = new DatabaseTargetContext("test", "AppDb");

        Assert.Equal("raw-value", anonymizer.Tokenize("raw-value", context, target));
    }

    [Fact]
    public void Tokenize_ShouldFallBackToMasking_WhenTokenizationNotUsable()
    {
        var vault = new TokenVault();
        var anonymizer = BuildAnonymizer(vault, enabled: true, tokenizationUsable: false);
        var context = new AnonymizationColumnContext("D", "Name", "dbo");
        var target = new DatabaseTargetContext("test", "AppDb");

        string masked = anonymizer.Tokenize("secret-value", context, target);

        AssertLooksHashed(masked);
        Assert.False(vault.TryResolve("§§§T1§§§", target, out _));
    }

    [Fact]
    public void Tokenize_ShouldThrow_WhenTargetIsNull()
    {
        var vault = new TokenVault();
        var anonymizer = BuildAnonymizer(vault, enabled: true);
        var context = new AnonymizationColumnContext("D", "Name", "dbo");

        Assert.Throws<ArgumentNullException>(
            () => anonymizer.Tokenize("value", context, (DatabaseTargetContext)null!));
    }
}
