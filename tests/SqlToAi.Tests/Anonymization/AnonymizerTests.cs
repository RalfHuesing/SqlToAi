#nullable enable

using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;

namespace SqlToAi.Tests.Anonymization;

// @covers SqlToAi.Anonymization.Anonymizer
public sealed class AnonymizerTests
{
    private static readonly Type TargetType = typeof(Anonymizer);

    [Fact]
    public void Anonymize_ShouldReturnOriginalValue_WhenDisabled()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = false;
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        // Act
        var result = anonymizer.Anonymize("LastName", "Mustermann");

        // Assert
        Assert.Equal("Mustermann", result);
    }

    [Fact]
    public void Anonymize_ShouldReturnOriginalValue_WhenValueIsEmptyOrNull()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        // Act & Assert
        Assert.Equal("", anonymizer.Anonymize("LastName", ""));
    }

    [Fact]
    public void Scramble_ShouldBeConsistentAndPreserveLengthCasingAndSpecialChars()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.DefaultMode = "ScramblePattern";
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        // Act
        var result1 = anonymizer.Anonymize("Name", "Ralf");
        var result2 = anonymizer.Anonymize("Name", "Ralf");
        var emailResult = anonymizer.Anonymize("Email", "Max.Mustermann@mail.de");

        // Assert
        // Consistency check
        Assert.Equal(result1, result2);
        
        // Casing and length preservation
        Assert.Equal(4, result1.Length);
        Assert.True(char.IsUpper(result1[0]));
        Assert.True(char.IsLower(result1[1]));
        Assert.True(char.IsLower(result1[2]));
        Assert.True(char.IsLower(result1[3]));
        
        // Not a simple static mask and not unchanged
        Assert.NotEqual("Xxxx", result1);
        Assert.NotEqual("Ralf", result1);

        // Email structure preservation
        Assert.Equal("Max.Mustermann@mail.de".Length, emailResult.Length);
        Assert.Equal('.', emailResult[3]);
        Assert.Equal('@', emailResult[14]);
        Assert.Equal('.', emailResult[19]);
        Assert.True(char.IsUpper(emailResult[0]));
        Assert.True(char.IsUpper(emailResult[4]));
    }

    [Fact]
    public void Hash_ShouldReturnConsistentSHA256HexStrings()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.DefaultMode = "Hash";
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        // Act
        var hash1 = anonymizer.Anonymize("CustomerName", "Ralf");
        var hash2 = anonymizer.Anonymize("CustomerName", "Ralf");
        var hash3 = anonymizer.Anonymize("CustomerName", "Hans");

        // Assert
        Assert.Equal(64, hash1.Length); // SHA-256 hex length is 64 chars
        Assert.Equal(hash1, hash2);      // Consistency check (reproducible)
        Assert.NotEqual(hash1, hash3);   // Distinct values yield distinct hashes
        
        // Assert lowercase hex chars
        Assert.Matches("^[0-9a-f]{64}$", hash1);
    }

    // -------------------------------------------------------------------------
    // Tests: Tokenize
    // -------------------------------------------------------------------------

    private static SqlToAiOptions BuildTokenizationOptions(bool enabled = true, string secret = "top-secret")
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.Tokenization.Enabled = enabled;
        options.Anonymizer.Tokenization.Secret = secret;
        return options;
    }

    [Fact]
    public void Tokenize_ShouldReturnOriginalValue_WhenValueIsEmptyOrNull()
    {
        var anonymizer = new Anonymizer(Options.Create(BuildTokenizationOptions()), new TokenVault());

        Assert.Equal("", anonymizer.Tokenize("IBAN", ""));
    }

    [Fact]
    public void Tokenize_ShouldBeDeterministic_ForTheSameValue()
    {
        var anonymizer = new Anonymizer(Options.Create(BuildTokenizationOptions()), new TokenVault());

        var token1 = anonymizer.Tokenize("IBAN", "DE89370400440532013000");
        var token2 = anonymizer.Tokenize("IBAN", "DE89370400440532013000");

        Assert.Equal(token1, token2);
    }

    [Fact]
    public void Tokenize_ShouldProduceDifferentTokens_ForDifferentValues()
    {
        var anonymizer = new Anonymizer(Options.Create(BuildTokenizationOptions()), new TokenVault());

        var token1 = anonymizer.Tokenize("IBAN", "DE89370400440532013000");
        var token2 = anonymizer.Tokenize("IBAN", "DE11520513735120710131");

        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void Tokenize_ShouldWrapTokenInConfiguredPrefixAndSuffix()
    {
        var options = BuildTokenizationOptions();
        options.Anonymizer.Tokenization.Prefix = "<<";
        options.Anonymizer.Tokenization.Suffix = ">>";
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        var token = anonymizer.Tokenize("IBAN", "DE89370400440532013000");

        Assert.StartsWith("<<", token, StringComparison.Ordinal);
        Assert.EndsWith(">>", token, StringComparison.Ordinal);
    }

    [Fact]
    public void Tokenize_ShouldProduceDifferentTokens_ForDifferentSecrets()
    {
        var vault = new TokenVault();
        var anonymizerA = new Anonymizer(Options.Create(BuildTokenizationOptions(secret: "secret-a")), vault);
        var anonymizerB = new Anonymizer(Options.Create(BuildTokenizationOptions(secret: "secret-b")), vault);

        var tokenA = anonymizerA.Tokenize("IBAN", "DE89370400440532013000");
        var tokenB = anonymizerB.Tokenize("IBAN", "DE89370400440532013000");

        Assert.NotEqual(tokenA, tokenB);
    }

    [Fact]
    public void Tokenize_ShouldStoreValueInVault_SoItCanBeResolvedBack()
    {
        var vault = new TokenVault();
        var anonymizer = new Anonymizer(Options.Create(BuildTokenizationOptions()), vault);

        var token = anonymizer.Tokenize("IBAN", "DE89370400440532013000");

        Assert.True(vault.TryResolve(token, out string? resolved));
        Assert.Equal("DE89370400440532013000", resolved);
    }

    [Fact]
    public void Tokenize_ShouldFallBackToMasking_WhenTokenizationDisabled()
    {
        var anonymizer = new Anonymizer(Options.Create(BuildTokenizationOptions(enabled: false)), new TokenVault());

        var result = anonymizer.Tokenize("Name", "Ralf");

        Assert.NotEqual("Ralf", result);
        Assert.Equal(4, result.Length); // ScramblePattern default preserves length
    }

    [Fact]
    public void Tokenize_ShouldFallBackToMasking_WhenSecretIsEmpty()
    {
        var anonymizer = new Anonymizer(Options.Create(BuildTokenizationOptions(secret: "")), new TokenVault());

        var result = anonymizer.Tokenize("Name", "Ralf");

        Assert.NotEqual("Ralf", result);
        Assert.Equal(4, result.Length);
    }

    [Fact]
    public void Tokenize_ShouldReturnOriginalValue_WhenGloballyDisabled()
    {
        var options = BuildTokenizationOptions();
        options.Anonymizer.Enabled = false;
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        Assert.Equal("Ralf", anonymizer.Tokenize("Name", "Ralf"));
    }
}
