#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Tests.Domain;

// @covers SqlToAi.Domain.SqlToAiError
public sealed class SqlToAiErrorTests
{
    private static readonly Type TargetType = typeof(SqlToAiError);

    [Fact]
    public void InvalidParameters_ShouldHaveCorrectCodeAndMessage()
    {
        // Act
        var error = SqlToAiError.InvalidParameters("Missing database");

        // Assert
        Assert.Equal(SqlToAiError.InvalidParametersCode, error.Code);
        Assert.Contains("Invalid parameters", error.Message);
        Assert.Contains("Missing database", error.Message);
    }

    [Fact]
    public void MultipleStatementsForbidden_ShouldHaveCorrectCodeAndMessage()
    {
        // Act
        var error = SqlToAiError.MultipleStatementsForbidden();

        // Assert
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, error.Code);
        Assert.Contains("is not allowed", error.Message);
    }

    [Fact]
    public void QueryError_ShouldHaveCorrectCodeAndMessage()
    {
        // Act
        var error = SqlToAiError.QueryError("Syntax error near SELECT");

        // Assert
        Assert.Equal(SqlToAiError.QueryErrorCode, error.Code);
        Assert.Contains("Syntax error near SELECT", error.Message);
    }

    [Fact]
    public void ObjectNotFound_ShouldHaveCorrectCodeAndMessage()
    {
        // Act
        var error = SqlToAiError.ObjectNotFound("dbo.Customers");

        // Assert
        Assert.Equal(SqlToAiError.ObjectNotFoundCode, error.Code);
        Assert.Contains("dbo.Customers", error.Message);
    }

    [Fact]
    public void SafetyCheckFailed_ShouldHaveCorrectCodeAndMessage()
    {
        // Act
        var error = SqlToAiError.SafetyCheckFailed("Blocked by blacklist");

        // Assert
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, error.Code);
        Assert.Contains("Blocked by blacklist", error.Message);
    }

    [Fact]
    public void InfrastructureError_ShouldHaveCorrectCodeAndMessage()
    {
        // Act
        var error = SqlToAiError.InfrastructureError("Connection timeout");

        // Assert
        Assert.Equal(SqlToAiError.InfrastructureErrorCode, error.Code);
        Assert.Contains("Connection timeout", error.Message);
    }

    [Fact]
    public void Timeout_ShouldHaveCorrectCodeAndMessage()
    {
        // Act
        var error = SqlToAiError.Timeout();

        // Assert
        Assert.Equal(SqlToAiError.TimeoutCode, error.Code);
        Assert.Contains("exceeded the configured time limit", error.Message);
    }

    [Fact]
    public void WriteOperationBlocked_ShouldHaveCorrectCodeAndMessage()
    {
        // Act
        var error = SqlToAiError.WriteOperationBlocked();

        // Assert
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, error.Code);
        Assert.Contains("Write operation blocked", error.Message);
    }

    [Fact]
    public void InvalidReferenceType_ShouldHaveCorrectCodeAndMessage()
    {
        // Act
        var error = SqlToAiError.InvalidReferenceType("dbo.MyProc");

        // Assert
        Assert.Equal(SqlToAiError.InvalidReferenceTypeCode, error.Code);
        Assert.Contains("dbo.MyProc", error.Message);
    }

    [Fact]
    public void InvalidParameterType_ShouldHaveCorrectCodeAndMessage()
    {
        // Act
        var error = SqlToAiError.InvalidParameterType("dbo.MyTable");

        // Assert
        Assert.Equal(SqlToAiError.InvalidParameterTypeCode, error.Code);
        Assert.Contains("dbo.MyTable", error.Message);
    }

    [Fact]
    public void InvalidDetailQueryType_ShouldHaveCorrectCodeAndMessage()
    {
        // Act
        var error = SqlToAiError.InvalidDetailQueryType("dbo.MyProc");

        // Assert
        Assert.Equal(SqlToAiError.InvalidDetailQueryTypeCode, error.Code);
        Assert.Contains("dbo.MyProc", error.Message);
    }

    [Fact]
    public void FileNotFound_ShouldHaveCorrectCodeAndPath()
    {
        // Act
        var error = SqlToAiError.FileNotFound("C:\\scripts\\missing.sql");

        // Assert
        Assert.Equal("SQL-AI-0111", error.Code);
        Assert.Contains("C:\\scripts\\missing.sql", error.Message);
    }

    [Fact]
    public void FileTooLarge_ShouldHaveCorrectCodeAndSizeContext()
    {
        // Act
        var error = SqlToAiError.FileTooLarge("C:\\scripts\\large.sql", 2049, 2048);

        // Assert
        Assert.Equal("SQL-AI-0112", error.Code);
        Assert.Contains("2049", error.Message);
        Assert.Contains("2048", error.Message);
        Assert.Contains("large.sql", error.Message);
    }

    [Fact]
    public void InvalidFileExtension_ShouldHaveCorrectCodeAndPath()
    {
        // Act
        var error = SqlToAiError.InvalidFileExtension("C:\\scripts\\query.txt");

        // Assert
        Assert.Equal("SQL-AI-0113", error.Code);
        Assert.Contains("query.txt", error.Message);
        Assert.Contains(".sql", error.Message);
    }
}
