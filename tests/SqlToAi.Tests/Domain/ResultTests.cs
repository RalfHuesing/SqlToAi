#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Tests.Domain;

// @covers SqlToAi.Domain.Result
public sealed class ResultTests
{
    private static readonly Type TargetType = typeof(Result);
    private static readonly Type TargetGenericType = typeof(Result<>);

    [Fact]
    public void SuccessResult_ShouldHaveSuccessState()
    {
        // Act
        var result = Result.Success();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void FailureResult_ShouldHaveFailureStateAndError()
    {
        // Arrange
        var error = new SqlToAiError("ERR-01", "Something went wrong");

        // Act
        var result = Result.Failure(error);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void SuccessResult_AccessingError_ShouldThrow()
    {
        // Arrange
        var result = Result.Success();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void FailureResult_AccessingError_ShouldNotThrow()
    {
        // Arrange
        var error = new SqlToAiError("ERR-01", "Error message");
        var result = Result.Failure(error);

        // Act
        var retrievedError = result.Error;

        // Assert
        Assert.Equal(error, retrievedError);
    }

    [Fact]
    public void SuccessGenericResult_ShouldHaveValueAndSuccessState()
    {
        // Act
        var result = Result<string>.Success("test-value");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal("test-value", result.Value);
    }

    [Fact]
    public void FailureGenericResult_ShouldHaveErrorAndFailureState()
    {
        // Arrange
        var error = new SqlToAiError("ERR-02", "Generic failure");

        // Act
        var result = Result<int>.Failure(error);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void SuccessGenericResult_AccessingError_ShouldThrow()
    {
        // Arrange
        var result = Result<int>.Success(42);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void FailureGenericResult_AccessingValue_ShouldThrow()
    {
        // Arrange
        var error = new SqlToAiError("ERR-03", "Generic failure");
        var result = Result<int>.Failure(error);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ImplicitOperator_FromValue_ShouldCreateSuccessResult()
    {
        // Act
        Result<string> result = "implicit-value";

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("implicit-value", result.Value);
    }

    [Fact]
    public void ImplicitOperator_FromError_ShouldCreateFailureResult()
    {
        // Arrange
        var error = new SqlToAiError("ERR-04", "Implicit error");

        // Act
        Result<string> result = error;

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }
}
