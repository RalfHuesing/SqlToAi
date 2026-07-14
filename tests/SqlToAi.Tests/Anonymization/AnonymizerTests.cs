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
    public void Scramble_ShouldBeConsistentAndPreserveLengthCasingAndSpecialChars()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.DefaultMode = "ScramblePattern";
        options.Anonymizer.ExcludedColumns = new List<string>();
        var anonymizer = new Anonymizer(Options.Create(options));

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
    public void Anonymize_ShouldAnonymizeAllColumnsByDefault_UnlessExcluded()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.ExcludedColumns = new List<string> { "Id" };
        var anonymizer = new Anonymizer(Options.Create(options));

        // Act
        var val1 = anonymizer.Anonymize("FirstName", "Ralf");
        var val2 = anonymizer.Anonymize("Description", "Nice project");
        var valExcluded = anonymizer.Anonymize("Id", "123");

        // Assert
        Assert.NotEqual("Ralf", val1);
        Assert.NotEqual("Nice project", val2);
        Assert.Equal("123", valExcluded);
    }

    [Fact]
    public void Anonymize_ShouldRespectDatabaseExclusions_WhenProvided()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.ExcludedColumns = new List<string> { "Id" };
        var anonymizer = new Anonymizer(Options.Create(options));

        var dbExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FakeProjects.ProjectName"
        };

        // Act
        // 1. Matched database-specific exclusion -> should NOT be anonymized
        var excludedVal = anonymizer.Anonymize("ProjectName", "SecretProject", "FakeProjects", dbExclusions);
        // 2. Not matched database-specific exclusion -> should be anonymized
        var normalVal = anonymizer.Anonymize("Description", "SecretDescription", "FakeProjects", dbExclusions);

        // Assert
        Assert.Equal("SecretProject", excludedVal);
        Assert.NotEqual("SecretDescription", normalVal);
    }
}
