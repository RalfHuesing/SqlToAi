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
    public void Anonymize_ShouldReturnOriginalValue_WhenColumnIsExcluded()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.ExcludedColumns = new List<string> { "Id", "*Id", "*Code", "Status" };
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

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
        options.Anonymizer.ExcludedColumns = new List<string>();
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

    [Fact]
    public void Anonymize_ShouldAnonymizeAllColumnsByDefault_UnlessExcluded()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.ExcludedColumns = new List<string> { "Id" };
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

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
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        var dbExclusions = new AnonymizerExclusionSet([new AnonymizerExclusionEntry(null, "FakeProjects", "ProjectName")]);

        // Act
        // 1. Matched database-specific exclusion -> should NOT be anonymized
        var excludedVal = anonymizer.Anonymize("SecretProject", new AnonymizationColumnContext("FakeProjects", "ProjectName", null, dbExclusions));
        // 2. Not matched database-specific exclusion -> should be anonymized
        var normalVal = anonymizer.Anonymize("SecretDescription", new AnonymizationColumnContext("FakeProjects", "Description", null, dbExclusions));

        // Assert
        Assert.Equal("SecretProject", excludedVal);
        Assert.NotEqual("SecretDescription", normalVal);
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
    public void Tokenize_ShouldReturnOriginalValue_WhenColumnMatchesGlobExclusion()
    {
        var options = BuildTokenizationOptions();
        options.Anonymizer.ExcludedColumns = new List<string> { "*Id", "Status" };
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        Assert.Equal("123", anonymizer.Tokenize("CustomerId", "123"));
        Assert.Equal("Active", anonymizer.Tokenize("Status", "Active"));
    }

    [Fact]
    public void Tokenize_ShouldReturnOriginalValue_WhenDatabaseExclusionMatches()
    {
        var options = BuildTokenizationOptions();
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());
        var dbExclusions = new AnonymizerExclusionSet([new AnonymizerExclusionEntry(null, "FakeProjects", "ProjectName")]);

        var result = anonymizer.Tokenize("SecretProject", new AnonymizationColumnContext("FakeProjects", "ProjectName", null, dbExclusions));

        Assert.Equal("SecretProject", result);
    }

    // -------------------------------------------------------------------------
    // Tests: alias-vs-origin exclusion decision (audit finding — see
    // tasks/audit-2026-07-24/01-security-guardrails.md, Finding 1)
    // -------------------------------------------------------------------------

    [Fact]
    public void Anonymize_ShouldAnonymize_WhenAliasMatchesExclusionPattern_ButOriginColumnDoesNot()
    {
        // "SELECT SSN AS RecordId" — the alias "RecordId" matches "*Id", but the real source
        // column is "SSN", which does not. Must still be anonymized despite the alias.
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.ExcludedColumns = new List<string> { "*Id" };
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        var result = anonymizer.Anonymize("123-45-6789", new AnonymizationColumnContext("Customers", "SSN", null, null));

        Assert.NotEqual("123-45-6789", result);
    }

    [Fact]
    public void Tokenize_ShouldTokenize_WhenAliasMatchesExclusionPattern_ButOriginColumnDoesNot()
    {
        var options = BuildTokenizationOptions();
        options.Anonymizer.ExcludedColumns = new List<string> { "*Id" };
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        var result = anonymizer.Tokenize("123-45-6789", new AnonymizationColumnContext("Customers", "SSN", null, null));

        Assert.NotEqual("123-45-6789", result);
    }

    [Fact]
    public void Anonymize_ShouldRespectExclusion_WhenAliasAndOriginColumnNameMatch()
    {
        // The common case (no aliasing): alias and resolved origin column name are identical —
        // existing exclusion behavior must be unchanged.
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.ExcludedColumns = new List<string> { "*Id" };
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        var result = anonymizer.Anonymize("123", new AnonymizationColumnContext("Customers", "CustomerId", null, null));

        Assert.Equal("123", result);
    }

    [Fact]
    public void Anonymize_ShouldNotExclude_WhenOriginColumnIsUnresolvable_EvenIfAliasWouldMatch()
    {
        // Fail-safe: when the real origin cannot be determined (e.g. a computed/literal/aggregate
        // expression, or a provider without schema-table support), the column must never be
        // excluded via the plain pattern list just because its alias happens to match.
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        options.Anonymizer.ExcludedColumns = new List<string> { "*Id" };
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        var result = anonymizer.Anonymize("SecretValue", new AnonymizationColumnContext("Customers", null, null, null));

        Assert.NotEqual("SecretValue", result);
    }

    [Fact]
    public void Tokenize_ShouldNotExclude_WhenOriginColumnIsUnresolvable_EvenIfAliasWouldMatch()
    {
        var options = BuildTokenizationOptions();
        options.Anonymizer.ExcludedColumns = new List<string> { "*Id" };
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());

        var result = anonymizer.Tokenize("SecretValue", new AnonymizationColumnContext("Customers", null, null, null));

        Assert.NotEqual("SecretValue", result);
    }

    [Fact]
    public void Anonymize_ShouldRespectDbExclusion_ByOriginColumnName_NotAlias()
    {
        // The database-specific exclusion table is keyed by the real column name too — an alias
        // must not let a non-excluded column masquerade as an excluded one, nor vice versa.
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());
        var dbExclusions = new AnonymizerExclusionSet([new AnonymizerExclusionEntry(null, "Customers", "SSN")]);

        // Alias "RecordId" does not appear in dbExclusions, but the real origin "SSN" does.
        var result = anonymizer.Anonymize("123-45-6789", new AnonymizationColumnContext("Customers", "SSN", null, dbExclusions));

        Assert.Equal("123-45-6789", result);
    }

    // -------------------------------------------------------------------------
    // Tests: schema-scoped database exclusions (audit finding — see
    // tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md, Finding "Ausschluss-/Regel-Abgleich
    // ist schema-blind — gleichnamige Tabelle in anderem Schema erbt fremde Freigabe")
    // -------------------------------------------------------------------------

    [Fact]
    public void Anonymize_ShouldRespectSchemaScopedDbExclusion_ForMatchingSchemaOnly()
    {
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());
        var dbExclusions = new AnonymizerExclusionSet([new AnonymizerExclusionEntry("dbo", "Kunden", "Email")]);

        var dboResult = anonymizer.Anonymize("dbo-clear@example.com", new AnonymizationColumnContext("Kunden", "Email", "dbo", dbExclusions));
        var archivResult = anonymizer.Anonymize("archiv-secret@example.com", new AnonymizationColumnContext("Kunden", "Email", "Archiv", dbExclusions));

        // The exclusion is scoped to schema "dbo" only; the same-named table in schema "Archiv"
        // must still be anonymized.
        Assert.Equal("dbo-clear@example.com", dboResult);
        Assert.NotEqual("archiv-secret@example.com", archivResult);
    }

    [Fact]
    public void Anonymize_ShouldRespectDbExclusion_AcrossEverySchema_WhenNoSchemaScopeIsSet()
    {
        // Backward-compatibility regression: an exclusion with no schema qualifier (the historical
        // configuration shape) must keep applying across every schema.
        var options = new SqlToAiOptions();
        options.Anonymizer.Enabled = true;
        var anonymizer = new Anonymizer(Options.Create(options), new TokenVault());
        var dbExclusions = new AnonymizerExclusionSet([new AnonymizerExclusionEntry(null, "Kunden", "Email")]);

        var dboResult = anonymizer.Anonymize("dbo-value", new AnonymizationColumnContext("Kunden", "Email", "dbo", dbExclusions));
        var archivResult = anonymizer.Anonymize("archiv-value", new AnonymizationColumnContext("Kunden", "Email", "Archiv", dbExclusions));

        Assert.Equal("dbo-value", dboResult);
        Assert.Equal("archiv-value", archivResult);
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
