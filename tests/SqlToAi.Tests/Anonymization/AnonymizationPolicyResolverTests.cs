using Microsoft.Extensions.Options;
using SqlToAi.Anonymization;
using SqlToAi.Configuration;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Anonymization;

public sealed class AnonymizationPolicyResolverTests
{
    private static readonly Type TargetType = typeof(AnonymizationPolicyResolver);

    private static AnonymizationPolicyResolver BuildResolver(
        SqlToAiOptions? options = null,
        bool centralExcluded = false)
    {
        if (options == null)
        {
            options = new SqlToAiOptions().WithNamedServers("primary");
            options.Databases.ReadOnlyAnonymized = [Target.QualifiedName];
        }
        var ruleProvider = new FakeRuleProvider(centralExcluded);
        return new AnonymizationPolicyResolver(Options.Create(options), ruleProvider);
    }

    private static readonly DatabaseTargetContext Target = new("primary", "Db");

    [Fact]
    public async Task WillAnonymizeAsync_ShouldReturnFalse_WhenGloballyDisabled()
    {
        var options = new SqlToAiOptions().WithNamedServers(Target.ServerAlias);
        options.Databases.ReadOnlyAnonymized = [Target.QualifiedName];
        options.Anonymizer.Enabled = false;
        var resolver = BuildResolver(options);

        bool result = await resolver.WillAnonymizeAsync(Target, "Table", "Column", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task WillAnonymizeAsync_ShouldReturnFalse_WhenCentralRuleExcludes()
    {
        var resolver = BuildResolver(centralExcluded: true);

        bool result = await resolver.WillAnonymizeAsync(Target, "FakeConsultants", "Phone", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task WillAnonymizeAsync_ShouldReturnTrue_WhenNothingExcludesTheColumn()
    {
        var resolver = BuildResolver();

        bool result = await resolver.WillAnonymizeAsync(Target, "Customers", "LastName", TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    [Theory]
    [InlineData(AccessLevel.ReadWrite)]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.SchemaOnly)]
    public async Task WillAnonymizeAsync_ShouldReturnFalse_WhenAccessLevelDoesNotRequireAnonymization(AccessLevel accessLevel)
    {
        var options = new SqlToAiOptions().WithNamedServers(Target.ServerAlias);
        switch (accessLevel)
        {
            case AccessLevel.ReadWrite:
                options.Databases.ReadWrite = [Target.QualifiedName];
                break;
            case AccessLevel.ReadOnly:
                options.Databases.ReadOnly = [Target.QualifiedName];
                break;
            case AccessLevel.SchemaOnly:
                options.Databases.SchemaOnly = [Target.QualifiedName];
                break;
        }

        var resolver = BuildResolver(options);
        bool result = await resolver.WillAnonymizeAsync(Target, "Customers", "LastName", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Theory]
    [InlineData(AccessLevel.ReadWriteAnonymized)]
    [InlineData(AccessLevel.ReadOnlyAnonymized)]
    public async Task WillAnonymizeAsync_ShouldReturnTrue_WhenAccessLevelRequiresAnonymization(AccessLevel accessLevel)
    {
        var options = new SqlToAiOptions().WithNamedServers(Target.ServerAlias);
        if (accessLevel == AccessLevel.ReadWriteAnonymized)
        {
            options.Databases.ReadWriteAnonymized = [Target.QualifiedName];
        }
        else
        {
            options.Databases.ReadOnlyAnonymized = [Target.QualifiedName];
        }

        var resolver = BuildResolver(options);
        bool result = await resolver.WillAnonymizeAsync(Target, "Customers", "LastName", TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // Tests: IsTokenizationActive
    // -------------------------------------------------------------------------

    [Fact]
    public void IsTokenizationActive_ShouldBeFalse_WhenTokenizationDisabled()
    {
        var options = new SqlToAiOptions().WithNamedServers(Target.ServerAlias);
        options.Anonymizer.Tokenization.Enabled = false;
        var resolver = BuildResolver(options);

        Assert.False(resolver.IsTokenizationActive);
    }

    [Fact]
    public void IsTokenizationActive_ShouldBeTrue_WhenEnabled()
    {
        var options = new SqlToAiOptions().WithNamedServers(Target.ServerAlias);
        options.Anonymizer.Tokenization.Enabled = true;
        var resolver = BuildResolver(options);

        Assert.True(resolver.IsTokenizationActive);
    }

    private sealed class FakeRuleProvider(bool excluded) : IAnonymizationRuleProvider
    {
        public Task<bool> IsExcludedAsync(DatabaseTargetContext target, string schemaName, string tableName, string columnName, CancellationToken cancellationToken = default)
            => Task.FromResult(excluded);
    }
}
