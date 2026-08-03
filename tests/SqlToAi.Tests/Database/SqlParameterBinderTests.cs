#nullable enable

using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using SqlToAi.Database;

namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for <see cref="SqlParameterBinder"/>, covering type inference, explicit DbType overrides,
/// null handling, dictionary parsing, and JSON document parameter binding.
/// </summary>
public sealed class SqlParameterBinderTests
{
    [Fact]
    public void BindParameters_NullCommand_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SqlParameterBinder.BindParameters(null!, new { }));
    }

    [Fact]
    public void BindParameters_NullOrEmptyParameters_DoesNotAddParameters()
    {
        using var command = new SqlCommand();

        SqlParameterBinder.BindParameters(command, null);
        Assert.Empty(command.Parameters);

        SqlParameterBinder.BindParameters(command, "");
        Assert.Empty(command.Parameters);
    }

    [Fact]
    public void BindParameters_AutoDetectsTypes_FromJsonObject()
    {
        using var command = new SqlCommand();
        string json = """
        {
            "Id": 42,
            "BigId": 9223372036854775807,
            "IsActive": true,
            "Amount": 12.34,
            "Name": "John Doe",
            "CreatedAt": "2026-08-03T10:00:00Z",
            "GlobalId": "d3b07384-d113-46a4-a719-d2d09c25f466"
        }
        """;

        using var doc = JsonDocument.Parse(json);
        SqlParameterBinder.BindParameters(command, doc.RootElement);

        Assert.Equal(7, command.Parameters.Count);

        var pId = command.Parameters["@Id"];
        Assert.Equal(42, pId.Value);
        Assert.Equal(DbType.Int32, pId.DbType);

        var pBigId = command.Parameters["@BigId"];
        Assert.Equal(9223372036854775807L, pBigId.Value);
        Assert.Equal(DbType.Int64, pBigId.DbType);

        var pActive = command.Parameters["@IsActive"];
        Assert.Equal(true, pActive.Value);
        Assert.Equal(DbType.Boolean, pActive.DbType);

        var pName = command.Parameters["@Name"];
        Assert.Equal("John Doe", pName.Value);
        Assert.Equal(DbType.String, pName.DbType);

        var pCreated = command.Parameters["@CreatedAt"];
        Assert.IsType<DateTime>(pCreated.Value);
        Assert.Equal(DbType.DateTime, pCreated.DbType);

        var pGuid = command.Parameters["@GlobalId"];
        Assert.IsType<Guid>(pGuid.Value);
        Assert.Equal(DbType.Guid, pGuid.DbType);
    }

    [Fact]
    public void BindParameters_ExplicitDbTypeOverride_JsonElement()
    {
        using var command = new SqlCommand();
        string json = """
        {
            "Code": {
                "value": "C123",
                "dbType": "AnsiString"
            },
            "NullableVal": {
                "value": null,
                "dbType": "Int32"
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        SqlParameterBinder.BindParameters(command, doc.RootElement);

        Assert.Equal(2, command.Parameters.Count);

        var pCode = command.Parameters["@Code"];
        Assert.Equal("C123", pCode.Value);
        Assert.Equal(DbType.AnsiString, pCode.DbType);

        var pNull = command.Parameters["@NullableVal"];
        Assert.Equal(DBNull.Value, pNull.Value);
        Assert.Equal(DbType.Int32, pNull.DbType);
    }

    [Fact]
    public void BindParameters_DictionaryInput_BindsCorrectly()
    {
        using var command = new SqlCommand();
        var dict = new Dictionary<string, object?>
        {
            ["@TenantId"] = 10,
            ["UserCode"] = "USR-1"
        };

        SqlParameterBinder.BindParameters(command, dict);

        Assert.Equal(2, command.Parameters.Count);
        Assert.Equal(10, command.Parameters["@TenantId"].Value);
        Assert.Equal("USR-1", command.Parameters["@UserCode"].Value);
    }

    [Fact]
    public void BindParameters_JsonStringInput_ParsesAndBinds()
    {
        using var command = new SqlCommand();
        string jsonString = "{\"Score\": 99.5}";

        SqlParameterBinder.BindParameters(command, jsonString);

        Assert.Single(command.Parameters);
        Assert.Equal("@Score", command.Parameters[0].ParameterName);
    }
}
