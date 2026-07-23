#nullable enable

using SqlToAi.Anonymization;

namespace SqlToAi.Tests.Anonymization;

// @covers SqlToAi.Anonymization.TokenVault
public sealed class TokenVaultTests
{
    [Fact]
    public void TryResolve_ShouldReturnFalse_ForUnknownToken()
    {
        var vault = new TokenVault();

        bool resolved = vault.TryResolve("§§§unknown§§§", out string? value);

        Assert.False(resolved);
        Assert.Null(value);
    }

    [Fact]
    public void TryResolve_ShouldReturnStoredValue_AfterStore()
    {
        var vault = new TokenVault();
        vault.Store("§§§abc§§§", "DE89370400440532013000");

        bool resolved = vault.TryResolve("§§§abc§§§", out string? value);

        Assert.True(resolved);
        Assert.Equal("DE89370400440532013000", value);
    }

    [Fact]
    public void Store_ShouldOverwrite_WhenCalledAgainWithSameToken()
    {
        var vault = new TokenVault();
        vault.Store("§§§abc§§§", "First");
        vault.Store("§§§abc§§§", "Second");

        vault.TryResolve("§§§abc§§§", out string? value);

        Assert.Equal("Second", value);
    }

    [Fact]
    public void TryResolve_ShouldBeCaseSensitive()
    {
        var vault = new TokenVault();
        vault.Store("§§§AbC§§§", "Value");

        Assert.False(vault.TryResolve("§§§abc§§§", out _));
    }
}
