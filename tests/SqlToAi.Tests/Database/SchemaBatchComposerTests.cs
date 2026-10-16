using System.Text;
using System.Text.Json;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

public sealed class SchemaBatchComposerTests
{
    [Fact]
    public async Task Compose_PreservesOrderAliasesErrorsAndCompleteTextsWithOneFetchPerObject()
    {
        SchemaObjectSelectionEntry[] selection = [Selected("Orders", 1), Missing("missing"), Selected("[dbo].[Orders]", 1), Selected("other", 2)];
        var calls = new List<int>();
        var response = await SchemaBatchComposer.ComposeAsync(selection, 65_536, (item, _) =>
        {
            calls.Add(item.Identity.ObjectId);
            return Task.FromResult<Result<string>>($"entire schema {item.Identity.ObjectId}");
        }, TestContext.Current.CancellationToken);

        Assert.False(response.Value.Complete);
        using var json = JsonDocument.Parse(response.Value.Json);
        var results = json.RootElement.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(selection.Select(item => item.ObjectName), results.Select(item => item.GetProperty("object_name").GetString()));
        Assert.Equal(["ok", "error", "ok", "ok"], results.Select(Status));
        Assert.Equal("entire schema 1", results[0].GetProperty("markdown").GetString());
        Assert.Equal(results[0].GetProperty("markdown").GetString(), results[2].GetProperty("markdown").GetString());
        Assert.Equal(SqlToAiError.ObjectNotFoundCode, results[1].GetProperty("error").GetProperty("code").GetString());
        Assert.Equal([1, 2], calls);
    }

    [Fact]
    public async Task Compose_UsesExactSerializedUtf8IncludingEscapesAndEnvelopeAtBoundary()
    {
        const string name = "dbo.名\"\\\t";
        string markdown = new('x', 200);
        markdown += "\n名😀\"\\\t";
        string expected = JsonSerializer.Serialize(new { complete = true, results = new[] { new { object_name = name, status = "ok", markdown } } });
        int exactBytes = Encoding.UTF8.GetByteCount(expected);
        var atLimit = await Compose([Selected(name)], exactBytes, markdown);
        Assert.True(atLimit.Value.Complete);
        Assert.Equal(expected, atLimit.Value.Json);
        Assert.Equal(exactBytes, Encoding.UTF8.GetByteCount(atLimit.Value.Json));

        var belowLimit = await Compose([Selected(name)], exactBytes - 1, markdown);
        Assert.False(belowLimit.Value.Complete);
        AssertOmitted(Assert.Single(Results(belowLimit.Value)));
        Assert.True(Encoding.UTF8.GetByteCount(belowLimit.Value.Json) <= exactBytes - 1);
    }

    [Fact]
    public async Task Compose_OmitsWholeSchemaAndEveryFollowingInputWithoutFetchingThem()
    {
        SchemaObjectSelectionEntry[] selection = [Selected("first", 1), Selected("second", 2), Missing("missing"), Selected("third", 3)];
        const string first = "complete first schema with sufficient content to exceed omission metadata";
        string expected = JsonSerializer.Serialize(new
        {
            complete = false,
            results = new object[]
            {
                new { object_name = "first", status = "ok", markdown = first },
                new { object_name = "second", status = "not_returned", reason = "response_limit" },
                new { object_name = "missing", status = "not_returned", reason = "response_limit" },
                new { object_name = "third", status = "not_returned", reason = "response_limit" }
            }
        });
        var calls = new List<int>();
        int limit = Encoding.UTF8.GetByteCount(expected);
        var response = await SchemaBatchComposer.ComposeAsync(selection, limit, (item, _) =>
        {
            calls.Add(item.Identity.ObjectId);
            return Task.FromResult<Result<string>>(item.Identity.ObjectId == 1 ? first : new string('x', 1000));
        }, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.Value.Json);
        Assert.False(response.Value.Complete);
        Assert.Equal([1, 2], calls);
        Assert.All(Results(response.Value).Skip(1), AssertOmitted);
    }

    [Fact]
    public async Task Compose_OversizedFirstSchemaReturnsEveryInputAsMetadataOnly()
    {
        SchemaObjectSelectionEntry[] selection = [Selected("first"), Selected("first"), Missing("missing")];
        var metadata = await Compose(selection, 65_536, new string('x', 100_000));
        int exactBytes = Encoding.UTF8.GetByteCount(metadata.Value.Json);
        int calls = 0;
        var response = await SchemaBatchComposer.ComposeAsync(selection, exactBytes, (_, _) =>
        {
            calls++;
            return Task.FromResult<Result<string>>(new string('x', 100_000));
        }, TestContext.Current.CancellationToken);

        Assert.Equal(metadata.Value.Json, response.Value.Json);
        Assert.All(Results(response.Value), AssertOmitted);
        Assert.Equal(1, calls);
        Assert.True(SchemaBatchComposer.ValidateRequest(selection.Select(item => item.ObjectName).ToArray(),
            new SchemaBatchOptions { MaxResponseBytes = exactBytes }).IsSuccess);
        Assert.True(SchemaBatchComposer.ValidateRequest(selection.Select(item => item.ObjectName).ToArray(),
            new SchemaBatchOptions { MaxResponseBytes = exactBytes - 1 }).IsFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compose_LongEscapedResolutionOrFetchErrorKeepsEarlierSuccessAndOmitsRemainder(bool fetchError)
    {
        var error = SqlToAiError.QueryError(string.Concat(Enumerable.Repeat("名\"\\\n", 1000)));
        SchemaObjectSelectionEntry[] selection = [Selected("first", 1), fetchError ? Selected("broken", 2) : new("broken", error), Selected("last", 3)];
        var calls = new List<int>();
        var response = await SchemaBatchComposer.ComposeAsync(selection, 500, (item, _) =>
        {
            calls.Add(item.Identity.ObjectId);
            return Task.FromResult<Result<string>>(item.Identity.ObjectId == 2 ? error : new string('x', 80));
        }, TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccess);
        var results = Results(response.Value);
        Assert.Equal("ok", Status(results[0]));
        Assert.All(results.Skip(1), AssertOmitted);
        Assert.Equal(fetchError ? [1, 2] : new[] { 1 }, calls);
        Assert.True(Encoding.UTF8.GetByteCount(response.Value.Json) <= 500);
    }

    [Fact]
    public async Task Compose_FetchErrorIsCompleteCatalogErrorAndLaterSiblingStillSucceeds()
    {
        var response = await SchemaBatchComposer.ComposeAsync([Selected("broken", 1), Selected("valid", 2)], 65_536,
            (item, _) => Task.FromResult<Result<string>>(item.Identity.ObjectId == 1 ? SqlToAiError.QueryError("fixed failure") : "full schema"),
            TestContext.Current.CancellationToken);
        var results = Results(response.Value);
        Assert.Equal(["error", "ok"], results.Select(Status));
        Assert.Contains("fixed failure", results[0].GetProperty("error").GetProperty("message").GetString());
        Assert.False(response.Value.Complete);
    }

    [Fact]
    public async Task Compose_ExactLimitIncludesFullCatalogErrorAndEveryEntryMetadata()
    {
        string markdown = new('x', 200);
        var error = SqlToAiError.ObjectNotFound("missing名\"\\");
        SchemaObjectSelectionEntry[] selection = [new("missing名\"\\", error), Selected("valid")];
        string expected = JsonSerializer.Serialize(new
        {
            complete = false,
            results = new object[]
            {
                new { object_name = "missing名\"\\", status = "error", error = new { code = error.Code, message = error.Message } },
                new { object_name = "valid", status = "ok", markdown }
            }
        });
        int exact = Encoding.UTF8.GetByteCount(expected);
        var atLimit = await Compose(selection, exact, markdown);
        Assert.Equal(expected, atLimit.Value.Json);
        Assert.False(atLimit.Value.Complete);
        var belowLimit = await Compose(selection, exact - 1, markdown);
        Assert.Equal("error", Status(Results(belowLimit.Value)[0]));
        AssertOmitted(Results(belowLimit.Value)[1]);
        Assert.True(Encoding.UTF8.GetByteCount(belowLimit.Value.Json) < exact);
    }

    [Fact]
    public async Task Compose_MetadataTooLargeRejectsBeforeAnySchemaFetch()
    {
        var response = await SchemaBatchComposer.ComposeAsync([Selected(new string('名', 1000))], 100, (_, _) =>
            throw new InvalidOperationException("must not fetch"), TestContext.Current.CancellationToken);
        Assert.Equal(SqlToAiError.InvalidParametersCode, response.Error.Code);
    }

    [Fact]
    public async Task Compose_CancellationStopsWithoutReturningPartialSuccess()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SchemaBatchComposer.ComposeAsync([Selected("name")], 65_536,
            (_, _) => throw new InvalidOperationException("must not fetch"), cancelled.Token));
    }

    [Fact]
    public async Task Compose_CancellationDuringFinalFetchCannotReturnSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SchemaBatchComposer.ComposeAsync([Selected("name")], 65_536,
            async (_, _) =>
            {
                await cancellation.CancelAsync();
                return "schema from a provider that ignored cancellation";
            }, cancellation.Token));
    }

    private static Task<Result<SchemaBatchResponse>> Compose(SchemaObjectSelectionEntry[] selection, int limit, string markdown) =>
        SchemaBatchComposer.ComposeAsync(selection, limit, (_, _) => Task.FromResult<Result<string>>(markdown), TestContext.Current.CancellationToken);

    private static SchemaObjectSelectionEntry Selected(string name, int id = 1) => new(name,
        new SchemaObject(new SchemaObjectIdentity(id, "dbo", name, SchemaObjectKind.Table)));
    private static SchemaObjectSelectionEntry Missing(string name) => new(name, SqlToAiError.ObjectNotFound(name));
    private static string? Status(JsonElement entry) => entry.GetProperty("status").GetString();
    private static JsonElement[] Results(SchemaBatchResponse response) =>
        JsonSerializer.Deserialize<JsonElement>(response.Json).GetProperty("results").EnumerateArray().ToArray();

    private static void AssertOmitted(JsonElement entry)
    {
        Assert.Equal("not_returned", Status(entry));
        Assert.Equal("response_limit", entry.GetProperty("reason").GetString());
        Assert.False(entry.TryGetProperty("markdown", out _));
        Assert.False(entry.TryGetProperty("error", out _));
    }
}
