#nullable enable

using SqlToAi.Database;
using Xunit;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.QueryDeconstructor
public sealed class QueryDeconstructorTests
{
    private static readonly System.Type TargetType = typeof(QueryDeconstructor);

    [Fact]
    public void Deconstruct_PlainSelect_ReturnsEmptyPreambleAndCtes()
    {
        const string sql = "SELECT * FROM Customers";
        var result = QueryDeconstructor.Deconstruct(sql);

        Assert.Equal(string.Empty, result.Preamble);
        Assert.Equal(string.Empty, result.Ctes);
        Assert.Equal("SELECT * FROM Customers", result.MainSelect);
    }

    [Fact]
    public void Deconstruct_PlainSelectWithSemicolon_TrimsSemicolonFromMainSelect()
    {
        const string sql = "SELECT Id, Name FROM Customers;   ";
        var result = QueryDeconstructor.Deconstruct(sql);

        Assert.Equal(string.Empty, result.Preamble);
        Assert.Equal(string.Empty, result.Ctes);
        Assert.Equal("SELECT Id, Name FROM Customers", result.MainSelect);
    }

    [Fact]
    public void Deconstruct_DeclarePreambleAndSelect_ExtractsPreamble()
    {
        const string sql = "DECLARE @x INT = 1;\nSELECT * FROM Customers WHERE Id = @x";
        var result = QueryDeconstructor.Deconstruct(sql);

        Assert.Equal("DECLARE @x INT = 1;", result.Preamble);
        Assert.Equal(string.Empty, result.Ctes);
        Assert.Equal("SELECT * FROM Customers WHERE Id = @x", result.MainSelect);
    }

    [Fact]
    public void Deconstruct_MultiplePreamblesAndSelect_ExtractsAllPreambles()
    {
        const string sql = "DECLARE @x INT = 1;\nSET @x = 2;\nSELECT * FROM Customers WHERE Id = @x;";
        var result = QueryDeconstructor.Deconstruct(sql);

        Assert.Equal("DECLARE @x INT = 1;\nSET @x = 2;", result.Preamble);
        Assert.Equal(string.Empty, result.Ctes);
        Assert.Equal("SELECT * FROM Customers WHERE Id = @x", result.MainSelect);
    }

    [Fact]
    public void Deconstruct_CteAndSelect_ExtractsCtes()
    {
        const string sql = "WITH Sub AS (SELECT Id FROM Customers) SELECT * FROM Sub";
        var result = QueryDeconstructor.Deconstruct(sql);

        Assert.Equal(string.Empty, result.Preamble);
        Assert.Equal("WITH Sub AS (SELECT Id FROM Customers)", result.Ctes);
        Assert.Equal("SELECT * FROM Sub", result.MainSelect);
    }

    [Fact]
    public void Deconstruct_CteWithExplicitColumns_ExtractsCtes()
    {
        const string sql = "WITH C1 (ColA, ColB) AS (SELECT 1 AS A, 2 AS B) SELECT ColA, ColB FROM C1;";
        var result = QueryDeconstructor.Deconstruct(sql);

        Assert.Equal(string.Empty, result.Preamble);
        Assert.Equal("WITH C1 (ColA, ColB) AS (SELECT 1 AS A, 2 AS B)", result.Ctes);
        Assert.Equal("SELECT ColA, ColB FROM C1", result.MainSelect);
    }

    [Fact]
    public void Deconstruct_DeclareAndCteAndSelect_ExtractsPreambleAndCtes()
    {
        const string sql = "DECLARE @x INT = 1;\nWITH Sub AS (SELECT Id FROM Customers WHERE Id = @x) SELECT * FROM Sub";
        var result = QueryDeconstructor.Deconstruct(sql);

        Assert.Equal("DECLARE @x INT = 1;", result.Preamble);
        Assert.Equal("WITH Sub AS (SELECT Id FROM Customers WHERE Id = @x)", result.Ctes);
        Assert.Equal("SELECT * FROM Sub", result.MainSelect);
    }

    [Fact]
    public void Deconstruct_ComplexNestedCteAndComments_ExtractsAccurately()
    {
        const string sql = "-- Header Comment\nWITH C1 AS (SELECT 1 AS A), C2 AS (SELECT A FROM C1 WHERE A IN (SELECT 1)) /* inline */ SELECT * FROM C2;";
        var result = QueryDeconstructor.Deconstruct(sql);

        Assert.Equal(string.Empty, result.Preamble);
        Assert.Equal("WITH C1 AS (SELECT 1 AS A), C2 AS (SELECT A FROM C1 WHERE A IN (SELECT 1))", result.Ctes);
        Assert.Equal("/* inline */ SELECT * FROM C2", result.MainSelect);
    }

    [Fact]
    public void Deconstruct_WithXmlNamespaces_ExtractsClause()
    {
        const string sql = "WITH XMLNAMESPACES ('http://example.com' AS ns) SELECT 1 AS X;";
        var result = QueryDeconstructor.Deconstruct(sql);

        Assert.Equal(string.Empty, result.Preamble);
        Assert.Equal("WITH XMLNAMESPACES ('http://example.com' AS ns)", result.Ctes);
        Assert.Equal("SELECT 1 AS X", result.MainSelect);
    }

    [Fact]
    public void CombineCtes_JoinsTwoCtesWithSingleWith()
    {
        const string cteA = "WITH C1 AS (SELECT 1 AS A)";
        const string cteB = "WITH C2 AS (SELECT 2 AS B)";

        string combined = QueryDeconstructor.CombineCtes(cteA, cteB);
        Assert.Equal("WITH C1 AS (SELECT 1 AS A), C2 AS (SELECT 2 AS B)", combined);
    }

    [Fact]
    public void CombinePreambles_DeDuplicatesStatements()
    {
        const string pA = "DECLARE @x INT = 1;\nDECLARE @y INT = 2;";
        const string pB = "DECLARE @x INT = 1;\nDECLARE @z INT = 3;";

        string combined = QueryDeconstructor.CombinePreambles(pA, pB);
        Assert.Equal("DECLARE @x INT = 1;\nDECLARE @y INT = 2;\nDECLARE @z INT = 3;", combined);
    }
}
