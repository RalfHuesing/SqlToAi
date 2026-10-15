using System.Net.Sockets;
using System.Reflection;
using Microsoft.Data.SqlClient;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Tests.TestSupport;
using System.ComponentModel;
using System.Security.Authentication;

namespace SqlToAi.Tests.Database;

public sealed class SqlToAiErrorMapperTests
{
    [Theory]
    [InlineData(-2146893019, "Die Zertifikatkette wurde von einer nicht vertrauenswürdigen Zertifizierungsstelle ausgestellt.")]
    [InlineData(-2146893022, "The target principal name is incorrect.")]
    [InlineData(-2146762495, "A required certificate is not within its validity period.")]
    [InlineData(0, "A connection was established but an error occurred during login (provider: SSL Provider, error: 0).")]
    [InlineData(0, "The pre-login TLS handshake failed.")]
    public void MapException_TlsFailure_DiscardsRawAndCustomMessages(int number, string message)
    {
        var exception = SqlExceptionFactory.Create(number, message + " Password=secret-marker;Server=raw-marker;",
            inner: new InvalidOperationException("inner-secret-marker"));

        var error = SqlToAiErrorMapper.MapException(exception, "custom-secret-marker");

        Assert.Equal(SqlToAiError.InfrastructureErrorCode, error.Code);
        Assert.Contains("SqlServer", error.Message);
        Assert.Contains("TLS/certificate", error.Message);
        Assert.DoesNotContain("marker", error.Message);
    }

    [Fact]
    public void MapException_NestedNativeAndAuthenticationFailures_AreSafeInfrastructureErrors()
    {
        Exception[] failures = [
            new InvalidOperationException("raw-marker", new Win32Exception(unchecked((int)0x80090325), "inner-marker")),
            new AuthenticationException("raw-marker", new InvalidOperationException("inner-marker"))];
        foreach (var failure in failures)
        {
            var error = SqlToAiErrorMapper.MapException(failure);
            Assert.Equal(SqlToAiError.InfrastructureErrorCode, error.Code);
            Assert.DoesNotContain("marker", error.Message);
        }
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(121)]
    [InlineData(258)]
    public void MapException_ActualTimeoutDuringHandshake_RetainsTimeout(int number)
    {
        var exception = SqlExceptionFactory.Create(number, "Timeout while waiting for the pre-login TLS handshake.");
        Assert.Equal(SqlToAiError.TimeoutCode, SqlToAiErrorMapper.MapException(exception).Code);
    }

    [Fact]
    public void MapException_NestedTimeoutDuringHandshake_RetainsTimeout()
    {
        var exception = SqlExceptionFactory.Create(0, "SSL Provider: pre-login handshake failed.",
            inner: new TimeoutException("sensitive timeout details"));
        Assert.Equal(SqlToAiError.TimeoutCode, SqlToAiErrorMapper.MapException(exception).Code);
    }

    [Fact]
    public void MapException_OrdinarySqlFailure_RetainsQueryMessageAndCustomMessage()
    {
        var exception = SqlExceptionFactory.Create(102, "Incorrect syntax near SELECTX.");
        Assert.Equal(SqlToAiError.QueryError(exception.Message), SqlToAiErrorMapper.MapException(exception));
        Assert.Equal(SqlToAiError.QueryError("custom"), SqlToAiErrorMapper.MapException(exception, "custom"));
    }

    [Theory]
    [InlineData(207, "Invalid column name 'certificate'.")]
    [InlineData(207, "Invalid column name 'TLS'.")]
    [InlineData(50000, "Custom query error mentioning SSL Provider and TLS handshake.")]
    public void MapException_OrdinarySqlFailureWithTlsWords_RetainsQueryContract(int number, string message)
    {
        var exception = SqlExceptionFactory.Create(number, message);
        Assert.Equal(SqlToAiError.QueryError(exception.Message), SqlToAiErrorMapper.MapException(exception));
    }

    [Fact]
    public void MapException_ShouldReturnTimeout_ForTimeoutException()
    {
        var ex = new TimeoutException("Operation timed out.");
        var error = SqlToAiErrorMapper.MapException(ex);

        Assert.Equal(SqlToAiError.TimeoutCode, error.Code);
    }

    [Fact]
    public void MapException_ShouldReturnInfrastructureError_ForSocketException()
    {
        var ex = new SocketException((int)SocketError.ConnectionRefused);
        var error = SqlToAiErrorMapper.MapException(ex);

        Assert.Equal(SqlToAiError.InfrastructureErrorCode, error.Code);
    }

    [Fact]
    public void MapException_ShouldReturnInfrastructureError_ForNestedSocketException()
    {
        var inner = new SocketException((int)SocketError.HostNotFound);
        var ex = new InvalidOperationException("Connection failed", inner);
        var error = SqlToAiErrorMapper.MapException(ex);

        Assert.Equal(SqlToAiError.InfrastructureErrorCode, error.Code);
    }

    [Fact]
    public void MapException_ShouldReturnQueryError_ForGenericException()
    {
        var ex = new InvalidOperationException("Something went wrong");
        var error = SqlToAiErrorMapper.MapException(ex);

        Assert.Equal(SqlToAiError.QueryErrorCode, error.Code);
        Assert.Equal("Query error: Something went wrong", error.Message);
    }

    [Fact]
    public void MapException_ShouldUseCustomErrorMessage_WhenProvided()
    {
        var ex = new InvalidOperationException("Raw message");
        var error = SqlToAiErrorMapper.MapException(ex, "Custom anonymized error message");

        Assert.Equal(SqlToAiError.QueryErrorCode, error.Code);
        Assert.Equal("Query error: Custom anonymized error message", error.Message);
    }
}
