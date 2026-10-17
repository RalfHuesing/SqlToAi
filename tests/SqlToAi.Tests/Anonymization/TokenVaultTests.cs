using SqlToAi.Anonymization;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Anonymization;

public sealed class TokenVaultTests
{
    private static readonly DatabaseTargetContext Target = new("test", "DemoDB");

    [Fact]
    public void TryResolve_ShouldReturnFalse_ForUnknownToken()
    {
        var vault = new TokenVault();

        bool resolved = vault.TryResolve("§§§unknown§§§", Target, out string? value);

        Assert.False(resolved);
        Assert.Null(value);
    }

    [Fact]
    public void TryResolve_ShouldReturnStoredValue_AfterStore()
    {
        var vault = new TokenVault();
        vault.Store("§§§abc§§§", "DE89370400440532013000", Target);

        bool resolved = vault.TryResolve("§§§abc§§§", Target, out string? value);

        Assert.True(resolved);
        Assert.Equal("DE89370400440532013000", value);
    }

    [Fact]
    public void Store_ShouldOverwrite_WhenCalledAgainWithSameToken()
    {
        var vault = new TokenVault();
        vault.Store("§§§abc§§§", "First", Target);
        vault.Store("§§§abc§§§", "Second", Target);

        vault.TryResolve("§§§abc§§§", Target, out string? value);

        Assert.Equal("Second", value);
    }

    [Fact]
    public void TryResolve_ShouldBeCaseSensitive()
    {
        var vault = new TokenVault();
        vault.Store("§§§AbC§§§", "Value", Target);

        Assert.False(vault.TryResolve("§§§abc§§§", Target, out _));
    }
}
