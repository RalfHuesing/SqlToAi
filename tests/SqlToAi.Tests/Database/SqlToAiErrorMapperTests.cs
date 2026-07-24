#nullable enable

using System.Net.Sockets;
using System.Reflection;
using Microsoft.Data.SqlClient;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.SqlToAiErrorMapper
public sealed class SqlToAiErrorMapperTests
{
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
