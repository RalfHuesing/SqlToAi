using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Tests.TestSupport;

namespace SqlToAi.Tests.Anonymization;

public sealed class AnonymizerTests
{
    private static readonly DatabaseTargetContext Target = new("test", "DemoDB");
    private static AnonymizationColumnContext Col(string name) => new(null, name, null);
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

    [Fact]
    public void Tokenize_ShouldReturnOriginalValue_WhenValueIsEmptyOrNull()
    {
        var anonymizer = new Anonymizer(Options.Create(AnonymizationTestHelper.BuildTokenizationOptions()), new TokenVault());

        Assert.Equal("", anonymizer.Tokenize("", Col("IBAN"), Target));
    }

    [Fact]
    public void Tokenize_ShouldBeDeterministic_ForTheSameValue()
    {
        var anonymizer = new Anonymizer(Options.Create(AnonymizationTestHelper.BuildTokenizationOptions()), new TokenVault());

        var token1 = anonymizer.Tokenize("DE89370400440532013000", Col("IBAN"), Target);
        var token2 = anonymizer.Tokenize("DE89370400440532013000", Col("IBAN"), Target);

        Assert.Equal(token1, token2);
    }

    [Fact]
    public void Tokenize_ShouldProduceCompactShortToken()
    {
        var anonymizer = new Anonymizer(Options.Create(AnonymizationTestHelper.BuildTokenizationOptions()), new TokenVault());

        var token = anonymizer.Tokenize("DE89370400440532013000", Col("IBAN"), Target);

        Assert.Equal("§§§T1§§§", token);
    }

    [Fact]
    public void Tokenize_ShouldProduceDifferentTokens_ForDifferentValues()
    {
        var anonymizer = new Anonymizer(Options.Create(AnonymizationTestHelper.BuildTokenizationOptions()), new TokenVault());

        var token1 = anonymizer.Tokenize("DE89370400440532013000", Col("IBAN"), Target);
        var token2 = anonymizer.Tokenize("DE11520513735120710131", Col("IBAN"), Target);

        Assert.NotEqual(token1, token2);
        Assert.Equal("§§§T1§§§", token1);
        Assert.Equal("§§§T2§§§", token2);
    }

    [Fact]
    public void Tokenize_ShouldWrapTokenInConfiguredPrefixAndSuffix()
    {
        var options = AnonymizationTestHelper.BuildTokenizationOptions();
        options.Anonymizer.Tokenization.Prefix = "<<";
        options.Anonymizer.Tokenization.Suffix = ">>";
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        var token = anonymizer.Tokenize("DE89370400440532013000", Col("IBAN"), Target);

        Assert.StartsWith("<<", token, StringComparison.Ordinal);
        Assert.EndsWith(">>", token, StringComparison.Ordinal);
        Assert.Equal("<<T1>>", token);
    }

    [Fact]
    public void Tokenize_ShouldStoreValueInVault_SoItCanBeResolvedBack()
    {
        var vault = new TokenVault();
        var anonymizer = new Anonymizer(Options.Create(AnonymizationTestHelper.BuildTokenizationOptions()), vault);

        var token = anonymizer.Tokenize("DE89370400440532013000", Col("IBAN"), Target);

        Assert.True(vault.TryResolve(token, Target, out string? resolved));
        Assert.Equal("DE89370400440532013000", resolved);
    }

    [Fact]
    public void Tokenize_ShouldFallBackToMasking_WhenTokenizationDisabled()
    {
        var anonymizer = new Anonymizer(Options.Create(AnonymizationTestHelper.BuildTokenizationOptions(enabled: false)), new TokenVault());

        var result = anonymizer.Tokenize("Ralf", Col("Name"), Target);

        Assert.NotEqual("Ralf", result);
        Assert.Equal(4, result.Length); // ScramblePattern default preserves length
    }

    [Fact]
    public void Tokenize_ShouldReturnOriginalValue_WhenGloballyDisabled()
    {
        var options = AnonymizationTestHelper.BuildTokenizationOptions();
        options.Anonymizer.Enabled = false;
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        Assert.Equal("Ralf", anonymizer.Tokenize("Ralf", Col("Name"), Target));
    }
}
