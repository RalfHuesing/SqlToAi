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
        Assert.Contains("Ungültige Parameter", error.Message);
        Assert.Contains("Missing database", error.Message);
    }

    [Fact]
    public void MultipleStatementsForbidden_ShouldHaveCorrectCodeAndMessage()
    {
        // Act
        var error = SqlToAiError.MultipleStatementsForbidden();

        // Assert
        Assert.Equal(SqlToAiError.MultipleStatementsForbiddenCode, error.Code);
        Assert.Contains("nicht erlaubt", error.Message);
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
        Assert.Contains("Zeitlimit überschritten", error.Message);
    }

    [Fact]
    public void WriteOperationBlocked_ShouldHaveCorrectCodeAndMessage()
    {
        // Act
        var error = SqlToAiError.WriteOperationBlocked();

        // Assert
        Assert.Equal(SqlToAiError.WriteOperationBlockedCode, error.Code);
        Assert.Contains("Schreiboperation blockiert", error.Message);
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
}
