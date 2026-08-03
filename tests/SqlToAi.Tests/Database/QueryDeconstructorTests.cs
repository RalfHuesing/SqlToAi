#nullable enable

using SqlToAi.Database;
using Xunit;

namespace SqlToAi.Tests.Database;

public sealed class QueryDeconstructorTests
{
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
    public void Deconstruct_DeclarePreambleAndSelect_ExtractsPreamble()
    {
        const string sql = "DECLARE @x INT = 1;\nSELECT * FROM Customers WHERE Id = @x";
        var result = QueryDeconstructor.Deconstruct(sql);

        Assert.Equal("DECLARE @x INT = 1;", result.Preamble);
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
    public void Deconstruct_DeclareAndCteAndSelect_ExtractsPreambleAndCtes()
    {
        const string sql = "DECLARE @x INT = 1;\nWITH Sub AS (SELECT Id FROM Customers WHERE Id = @x) SELECT * FROM Sub";
        var result = QueryDeconstructor.Deconstruct(sql);

        Assert.Equal("DECLARE @x INT = 1;", result.Preamble);
        Assert.Equal("WITH Sub AS (SELECT Id FROM Customers WHERE Id = @x)", result.Ctes);
        Assert.Equal("SELECT * FROM Sub", result.MainSelect);
    }

    [Fact]
    public void CombineCtes_JoinsTwoCtesWithSingleWith()
    {
        const string cteA = "WITH C1 AS (SELECT 1 AS A)";
        const string cteB = "WITH C2 AS (SELECT 2 AS B)";

        string combined = QueryDeconstructor.CombineCtes(cteA, cteB);
        Assert.Equal("WITH C1 AS (SELECT 1 AS A), C2 AS (SELECT 2 AS B)", combined);
    }
}
