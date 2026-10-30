using SqlToAi.Database;

namespace SqlToAi.Tests.Database;

public sealed class SqlMetadataQueryDetectorTests
{
    [Theory]
    [InlineData("SELECT c.name FROM sys.columns AS c")]
    [InlineData("SELECT c.name FROM [sys].[CoLuMnS] AS c")]
    [InlineData("SELECT c.name FROM [DemoDb].[sys].[columns] AS c")]
    [InlineData("SELECT m.definition FROM sys.sql_modules AS m")]
    [InlineData("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS")]
    [InlineData("SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.Sample'))")]
    public void ContainsSchemaMetadataReference_DetectsCatalogAndDefinitionQueries(string query)
    {
        Assert.True(SqlMetadataQueryDetector.ContainsSchemaMetadataReference(query));
    }

    [Theory]
    [InlineData("SELECT 'sys.columns OBJECT_DEFINITION' AS value")]
    [InlineData("-- sys.columns and OBJECT_DEFINITION are only mentioned here\nSELECT 1")]
    [InlineData("SELECT dbo.OBJECT_DEFINITION(OBJECT_ID(N'dbo.Sample'))")]
    [InlineData("SELECT c.name FROM dbo.columns AS c")]
    [InlineData("SELECT m.definition FROM dbo.sql_modules AS m")]
    [InlineData("SELECT * FROM sys.dm_exec_query_stats")]
    public void ContainsSchemaMetadataReference_IgnoresStringsCommentsUserTablesAndDmvQueries(string query)
    {
        Assert.False(SqlMetadataQueryDetector.ContainsSchemaMetadataReference(query));
    }

    [Fact]
    public void ContainsSchemaMetadataReference_ReturnsFalseForParseErrors()
    {
        Assert.False(SqlMetadataQueryDetector.ContainsSchemaMetadataReference("SELECT FROM"));
    }

    [Fact]
    public void GetResponseHint_WhenCatalogQuery_ReturnsStandardHint()
    {
        string? hint = SqlMetadataQueryDetector.GetResponseHint("SELECT * FROM sys.tables");
        Assert.Equal("Schema hint: Direct catalog results may be anonymized. Use sql_get_schema for columns and view/routine definitions, sql_get_trigger_definition for triggers, or sql_search_objects to find object names.", hint);
    }
}
