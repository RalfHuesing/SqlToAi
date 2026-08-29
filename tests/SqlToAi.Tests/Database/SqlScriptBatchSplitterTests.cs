#nullable enable

using SqlToAi.Database;
using Xunit;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.SqlScriptBatchSplitter
public sealed class SqlScriptBatchSplitterTests
{
    private static readonly System.Type TargetType = typeof(SqlScriptBatchSplitter);

    [Fact]
    public void NullOrWhitespace_ReturnsEmptyList()
    {
        Assert.Empty(SqlScriptBatchSplitter.Split(null));
        Assert.Empty(SqlScriptBatchSplitter.Split(" \t\r\n "));
    }

    [Fact]
    public void ScriptWithoutSeparator_PreservesTextAndLineRange()
    {
        const string script = "SELECT 1\r\n-- retained comment\nSELECT 2";

        var batch = Assert.Single(SqlScriptBatchSplitter.Split(script));

        Assert.Equal(script, batch.Text);
        Assert.Equal(1, batch.StartLine);
        Assert.Equal(3, batch.EndLine);
        Assert.Equal(1, batch.RepeatCount);
    }

    [Fact]
    public void GoVariants_SplitBatchesAndTrackRepeatCounts()
    {
        const string script = "SELECT 1\r\n \tgO 3  \r\nSELECT 2\n GO /* separator */ \n SELECT 3";

        var batches = SqlScriptBatchSplitter.Split(script);

        Assert.Equal(3, batches.Count);
        Assert.Equal("SELECT 1\r\n", batches[0].Text);
        Assert.Equal(1, batches[0].StartLine);
        Assert.Equal(1, batches[0].EndLine);
        Assert.Equal(3, batches[0].RepeatCount);
        Assert.Equal("SELECT 2\n", batches[1].Text);
        Assert.Equal(3, batches[1].StartLine);
        Assert.Equal(3, batches[1].EndLine);
        Assert.Equal(1, batches[1].RepeatCount);
        Assert.Equal(" SELECT 3", batches[2].Text);
        Assert.Equal(5, batches[2].StartLine);
        Assert.Equal(5, batches[2].EndLine);
        Assert.Equal(1, batches[2].RepeatCount);
    }

    [Fact]
    public void GoWithLineComment_IsAcceptedAsSeparator()
    {
        const string script = "SELECT 1\nGO -- separator comment\nSELECT 2";

        var batches = SqlScriptBatchSplitter.Split(script);

        Assert.Equal(2, batches.Count);
        Assert.Equal("SELECT 1\n", batches[0].Text);
        Assert.Equal(1, batches[0].RepeatCount);
        Assert.Equal("SELECT 2", batches[1].Text);
        Assert.Equal(3, batches[1].StartLine);
        Assert.Equal(3, batches[1].EndLine);
    }

    [Theory]
    [InlineData("GO 0")]
    [InlineData("GO -1")]
    [InlineData("GO 1.5")]
    [InlineData("GO 2147483648")]
    public void InvalidRepeatCount_IsRetainedAsBatchText(string separator)
    {
        string script = $"SELECT 1\n{separator}\nSELECT 2";

        var batch = Assert.Single(SqlScriptBatchSplitter.Split(script));

        Assert.Equal(script, batch.Text);
        Assert.Equal(1, batch.StartLine);
        Assert.Equal(3, batch.EndLine);
        Assert.Equal(1, batch.RepeatCount);
    }

    [Fact]
    public void GoInsideLiteralOrLineComment_DoesNotSplit()
    {
        const string script = "SELECT 'GO' AS Value\n-- GO\nSELECT GOLD AS Value";

        var batch = Assert.Single(SqlScriptBatchSplitter.Split(script));

        Assert.Equal(script, batch.Text);
        Assert.Equal(1, batch.StartLine);
        Assert.Equal(3, batch.EndLine);
    }

    [Fact]
    public void GoInsideMultilineBlockComment_DoesNotSplit()
    {
        const string script = "/* start\nGO\nend */\nSELECT 1\nGO\nSELECT 2";

        var batches = SqlScriptBatchSplitter.Split(script);

        Assert.Equal(2, batches.Count);
        Assert.Equal("/* start\nGO\nend */\nSELECT 1\n", batches[0].Text);
        Assert.Equal(1, batches[0].StartLine);
        Assert.Equal(4, batches[0].EndLine);
        Assert.Equal("SELECT 2", batches[1].Text);
        Assert.Equal(6, batches[1].StartLine);
        Assert.Equal(6, batches[1].EndLine);
    }

    [Fact]
    public void NestedMultilineBlockComment_DoesNotSplitAtInnerGo()
    {
        const string script = "SELECT 1;\nGO\n/* outer\n   /* nested\n   */\n   GO\n*/";

        var batches = SqlScriptBatchSplitter.Split(script);

        Assert.Equal(2, batches.Count);
        Assert.Equal("SELECT 1;\n", batches[0].Text);
        Assert.Equal(1, batches[0].StartLine);
        Assert.Equal(1, batches[0].EndLine);
        Assert.Equal("/* outer\n   /* nested\n   */\n   GO\n*/", batches[1].Text);
        Assert.Equal(3, batches[1].StartLine);
        Assert.Equal(7, batches[1].EndLine);
    }

    [Fact]
    public void NestedTrailingBlockComment_IsAcceptedAsSeparator()
    {
        const string script = "SELECT 1\nGO /* outer /* nested */ */\nSELECT 2";

        var batches = SqlScriptBatchSplitter.Split(script);

        Assert.Equal(2, batches.Count);
        Assert.Equal("SELECT 1\n", batches[0].Text);
        Assert.Equal(1, batches[0].StartLine);
        Assert.Equal(1, batches[0].EndLine);
        Assert.Equal("SELECT 2", batches[1].Text);
        Assert.Equal(3, batches[1].StartLine);
        Assert.Equal(3, batches[1].EndLine);
    }

    [Fact]
    public void EmptySectionsBetweenSeparators_AreOmitted()
    {
        const string script = "GO\n\nGO\n   \nGO\nSELECT 1";

        var batch = Assert.Single(SqlScriptBatchSplitter.Split(script));

        Assert.Equal("SELECT 1", batch.Text);
        Assert.Equal(6, batch.StartLine);
        Assert.Equal(6, batch.EndLine);
    }
}
