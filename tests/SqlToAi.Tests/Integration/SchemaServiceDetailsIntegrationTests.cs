#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class SchemaServiceDetailsIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _db;

    public SchemaServiceDetailsIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _db = TestConstants.DatabaseName;
    }

    [Fact]
    public async Task GetSchemaForeignKeysAsync_ShouldReturnResult_ForTable()
    {
        var result = await _fx.SchemaService.GetSchemaForeignKeysAsync(_db, "dbo.FakeProjects", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task GetSchemaForeignKeysAsync_ShouldMergeCompositeKeyColumns_IntoSingleRow()
    {
        var result = await _fx.SchemaService.GetSchemaForeignKeysAsync(_db, "dbo.FakeAddressCommunications", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));

        int occurrences = result.Value.Split("FK_FakeAddressCommunications_FakeAddresses", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, occurrences);

        string? fkLine = result.Value.Split('\n').FirstOrDefault(l => l.Contains("FK_FakeAddressCommunications_FakeAddresses", StringComparison.Ordinal));
        Assert.NotNull(fkLine);
        Assert.Contains("Adresse", fkLine, StringComparison.Ordinal);
        Assert.Contains("Mandant", fkLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSchemaIndexesAsync_ShouldReturnAtLeastOneIndex()
    {
        var result = await _fx.SchemaService.GetSchemaIndexesAsync(_db, "dbo.FakeProjects", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public async Task GetSchemaConstraintsAsync_ShouldNotFail()
    {
        var result = await _fx.SchemaService.GetSchemaConstraintsAsync(_db, "dbo.FakeProjects", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task GetRoutineParametersAsync_ShouldReturnParameters_ForKnownProcedure()
    {
        var result = await _fx.SchemaService.GetRoutineParametersAsync(_db, "dbo.spFakeSysTan", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public async Task GetObjectReferencesAsync_ShouldReturnResult_ForKnownTable()
    {
        var result = await _fx.SchemaService.GetObjectReferencesAsync(_db, "dbo.FakeProjects", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task GetSchemaForeignKeysAsync_ShouldFail_WhenObjectIsRoutine()
    {
        var result = await _fx.SchemaService.GetSchemaForeignKeysAsync(_db, "dbo.spFakeSysTan", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidDetailQueryTypeCode, result.Error.Code);
    }

    [Fact]
    public async Task GetSchemaIndexesAsync_ShouldFail_WhenObjectIsRoutine()
    {
        var result = await _fx.SchemaService.GetSchemaIndexesAsync(_db, "dbo.spFakeSysTan", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidDetailQueryTypeCode, result.Error.Code);
    }

    [Fact]
    public async Task GetSchemaConstraintsAsync_ShouldFail_WhenObjectIsRoutine()
    {
        var result = await _fx.SchemaService.GetSchemaConstraintsAsync(_db, "dbo.spFakeSysTan", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidDetailQueryTypeCode, result.Error.Code);
    }
}
