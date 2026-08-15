#nullable enable

// @covers SqlToAi.Database.TableSchemaRenderer
namespace SqlToAi.Tests.Database;

/// <summary>
/// Unit tests for the private <c>FormatTypeString</c> logic in
/// <see cref="SqlToAi.Database.TableSchemaRenderer"/>, accessed via reflection.
/// </summary>
public sealed class TableSchemaRendererTests
{
    // -------------------------------------------------------------------------
    // FormatTypeString — nvarchar/nchar byte-length correction
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("nvarchar", 200, "nvarchar(100)")]
    [InlineData("nvarchar", 20, "nvarchar(10)")]
    [InlineData("nvarchar", -1, "nvarchar(max)")]
    [InlineData("nchar", 10, "nchar(5)")]
    [InlineData("nchar", -1, "nchar(max)")]
    public void FormatTypeString_NvarcharAndNchar_DividesByTwo(string type, int maxLength, string expected)
    {
        string actual = InvokeFormatTypeString(type, maxLength, 0, 0);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("varchar", 50, "varchar(50)")]
    [InlineData("varchar", 255, "varchar(255)")]
    [InlineData("varchar", -1, "varchar(max)")]
    [InlineData("char", 10, "char(10)")]
    [InlineData("char", -1, "char(max)")]
    public void FormatTypeString_VarcharAndChar_DoesNotDivide(string type, int maxLength, string expected)
    {
        string actual = InvokeFormatTypeString(type, maxLength, 0, 0);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("decimal", 18, 2, "decimal(18,2)")]
    [InlineData("numeric", 10, 4, "numeric(10,4)")]
    public void FormatTypeString_Decimal_IncludesPrecisionAndScale(string type, int precision, int scale, string expected)
    {
        string actual = InvokeFormatTypeString(type, 0, precision, scale);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("int", 4, "int")]
    [InlineData("datetime", 8, "datetime")]
    [InlineData("bit", 1, "bit")]
    public void FormatTypeString_OtherTypes_ReturnTypeName(string type, int maxLength, string expected)
    {
        string actual = InvokeFormatTypeString(type, maxLength, 0, 0);
        Assert.Equal(expected, actual);
    }

    // -------------------------------------------------------------------------
    // Helper — invoke private static FormatTypeString via reflection
    // -------------------------------------------------------------------------

    private static string InvokeFormatTypeString(string type, int length, int precision, int scale)
    {
        var method = typeof(SqlToAi.Database.TableSchemaRenderer)
            .GetMethod("FormatTypeString",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("FormatTypeString not found.");

        return (string)(method.Invoke(null, [type, length, precision, scale])
            ?? throw new InvalidOperationException("FormatTypeString returned null."));
    }
}
