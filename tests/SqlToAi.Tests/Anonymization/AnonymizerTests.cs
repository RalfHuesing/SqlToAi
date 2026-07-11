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
        var anonymizer = new Anonymizer(Options.Create(options));

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
        var anonymizer = new Anonymizer(Options.Create(options));

        // Act & Assert
        Assert.Equal("", anonymizer.Anonymize("LastName", ""));
    }

    [Fact]
    public void Anonymize_ShouldReturnOriginalValue_WhenColumnIsExcluded()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.ExcludedColumns = new List<string> { "Id", "*Id", "*Code", "Status" };
        var anonymizer = new Anonymizer(Options.Create(options));

        // Act & Assert
        Assert.Equal("123", anonymizer.Anonymize("CustomerId", "123"));
        Assert.Equal("ABC-10", anonymizer.Anonymize("ProductCode", "ABC-10"));
        Assert.Equal("Active", anonymizer.Anonymize("Status", "Active"));
        // Matches none, should be anonymized
        Assert.NotEqual("Mustermann", anonymizer.Anonymize("LastName", "Mustermann"));
    }

    [Fact]
    public void Scramble_ShouldPreserveStructureCasingAndSpecialChars()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.DefaultMode = "ScramblePattern";
        options.Anonymizer.ExcludedColumns = new List<string>();
        var anonymizer = new Anonymizer(Options.Create(options));

        // Act
        var emailResult = anonymizer.Anonymize("Email", "Max.Mustermann@mail.de");
        var phoneResult = anonymizer.Anonymize("Phone", "+49 (123) 456-789");

        // Assert
        Assert.Equal("Xxx.Xxxxxxxxxx@xxxx.xx", emailResult);
        Assert.Equal("+99 (999) 999-999", phoneResult);
    }

    [Fact]
    public void Hash_ShouldReturnConsistentSHA256HexStrings()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.DefaultMode = "Hash";
        options.Anonymizer.ExcludedColumns = new List<string>();
        var anonymizer = new Anonymizer(Options.Create(options));

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

    [Fact]
    public void Anonymize_ShouldFollowMatchedRules()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.Rules = new List<AnonymizerRule>
        {
            new() { Pattern = "*name*", Mode = "ScramblePattern" },
            new() { Pattern = "*hash*", Mode = "Hash" }
        };
        var anonymizer = new Anonymizer(Options.Create(options));

        // Act & Assert
        // Column matching rule 1 -> Scramble
        Assert.Equal("Xxxx", anonymizer.Anonymize("FirstName", "Ralf"));

        // Column matching rule 2 -> Hash
        var hashed = anonymizer.Anonymize("UserSecureHash", "secret");
        Assert.Matches("^[0-9a-f]{64}$", hashed);

        // Column matching NO rules -> original value returned unchanged
        Assert.Equal("test-description", anonymizer.Anonymize("Description", "test-description"));
    }
}
