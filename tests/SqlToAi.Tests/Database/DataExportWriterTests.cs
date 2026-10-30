using System.Globalization;
using System.Text;
using System.Text.Json;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

public sealed class DataExportWriterTests
{
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CsvWriter_EscapesCommasQuotesNewlines()
    {
        using var stream = new MemoryStream();
        await using (var writer = new CsvDataRowWriter(stream))
        {
            var columns = new List<ExportColumnInfo>
            {
                new("col_plain", "string"),
                new("col,comma", "string"),
                new("col\"quote", "string"),
                new("col\nnewline", "string")
            };

            await writer.WriteHeaderAsync(columns, TestCt);

            var values = new object?[]
            {
                "Simple",
                "Hello, world",
                "She said \"Hello\"",
                "Line1\r\nLine2"
            };

            await writer.WriteRowAsync(columns, values, TestCt);
            await writer.FlushAsync(TestCt);
        }

        string csv = Encoding.UTF8.GetString(stream.ToArray());
        string expected = "col_plain,\"col,comma\",\"col\"\"quote\",\"col\nnewline\"\r\nSimple,\"Hello, world\",\"She said \"\"Hello\"\"\",\"Line1\r\nLine2\"\r\n";
        Assert.Equal(expected, csv);
    }

    [Fact]
    public async Task CsvWriter_DistinguishesNullFromEmptyString()
    {
        using var stream = new MemoryStream();
        await using (var writer = new CsvDataRowWriter(stream))
        {
            var columns = new List<ExportColumnInfo>
            {
                new("val_null", "string"),
                new("val_empty", "string"),
                new("val_dbnull", "string"),
                new("val_text", "string")
            };

            await writer.WriteHeaderAsync(columns, TestCt);

            var values = new object?[]
            {
                null,
                "",
                DBNull.Value,
                "data"
            };

            await writer.WriteRowAsync(columns, values, TestCt);
            await writer.FlushAsync(TestCt);
        }

        string csv = Encoding.UTF8.GetString(stream.ToArray());
        string[] lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal("val_null,val_empty,val_dbnull,val_text", lines[0]);
        // NULL must be unquoted empty field; "" must be quoted as ""; DBNull is unquoted empty field
        Assert.Equal(",\"\",,data", lines[1]);
    }

    [Fact]
    public async Task CsvWriter_UsesInvariantCultureForDecimalsAndDates()
    {
        using var stream = new MemoryStream();
        await using (var writer = new CsvDataRowWriter(stream))
        {
            var columns = new List<ExportColumnInfo>
            {
                new("amount", "decimal"),
                new("created_at", "datetime"),
                new("is_active", "bool"),
                new("raw_bytes", "binary")
            };

            await writer.WriteHeaderAsync(columns, TestCt);

            var now = new DateTime(2026, 10, 30, 14, 30, 0, DateTimeKind.Utc);
            byte[] bytes = [0xDE, 0xAD, 0xBE, 0xEF];

            var values = new object?[]
            {
                1234.56m,
                now,
                true,
                bytes
            };

            await writer.WriteRowAsync(columns, values, TestCt);
            await writer.FlushAsync(TestCt);
        }

        string csv = Encoding.UTF8.GetString(stream.ToArray());
        string[] lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal("amount,created_at,is_active,raw_bytes", lines[0]);
        Assert.Equal("1234.56,2026-10-30T14:30:00.0000000Z,true,3q2+7w==", lines[1]);
    }

    [Fact]
    public async Task JsonlWriter_OutputsOneJsonObjectPerLineWithTypedValues()
    {
        using var stream = new MemoryStream();
        await using (var writer = new JsonlDataRowWriter(stream))
        {
            var columns = new List<ExportColumnInfo>
            {
                new("id", "int"),
                new("name", "string"),
                new("active", "bool"),
                new("score", "decimal"),
                new("extra", "string")
            };

            await writer.WriteHeaderAsync(columns, TestCt);

            var row1 = new object?[] { 1, "Alice", true, 99.5m, null };
            var row2 = new object?[] { 2, "Bob", false, 85.0m, DBNull.Value };

            await writer.WriteRowAsync(columns, row1, TestCt);
            await writer.WriteRowAsync(columns, row2, TestCt);
            await writer.FlushAsync(TestCt);
        }

        string jsonl = Encoding.UTF8.GetString(stream.ToArray());
        string[] lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);

        using var doc1 = JsonDocument.Parse(lines[0]);
        Assert.Equal(1, doc1.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Alice", doc1.RootElement.GetProperty("name").GetString());
        Assert.True(doc1.RootElement.GetProperty("active").GetBoolean());
        Assert.Equal(99.5m, doc1.RootElement.GetProperty("score").GetDecimal());
        Assert.Equal(JsonValueKind.Null, doc1.RootElement.GetProperty("extra").ValueKind);

        using var doc2 = JsonDocument.Parse(lines[1]);
        Assert.Equal(2, doc2.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Bob", doc2.RootElement.GetProperty("name").GetString());
        Assert.False(doc2.RootElement.GetProperty("active").GetBoolean());
        Assert.Equal(85.0m, doc2.RootElement.GetProperty("score").GetDecimal());
        Assert.Equal(JsonValueKind.Null, doc2.RootElement.GetProperty("extra").ValueKind);
    }

    [Theory]
    [InlineData("csv", @"C:\path\file.txt", null, "csv")]
    [InlineData("CSV", @"C:\path\file.jsonl", null, "csv")]
    [InlineData("jsonl", @"C:\path\file.csv", null, "jsonl")]
    [InlineData("JSONL", @"C:\path\file.txt", null, "jsonl")]
    [InlineData(null, @"C:\path\file.csv", null, "csv")]
    [InlineData("", @"C:\path\file.CSV", null, "csv")]
    [InlineData(null, @"C:\path\file.jsonl", null, "jsonl")]
    [InlineData(" ", @"C:\path\file.JSONL", null, "jsonl")]
    [InlineData(null, @"C:\path\file.dat", "jsonl", "jsonl")]
    [InlineData(null, @"C:\path\file.dat", "csv", "csv")]
    [InlineData(null, @"C:\path\file.dat", null, "csv")]
    public void DataWriterFactory_ResolveFormat_ResolvesExpectedFormat(string? format, string outputPath, string? defaultFormat, string expected)
    {
        var result = DataWriterFactory.ResolveFormat(format, outputPath, defaultFormat);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void DataWriterFactory_ResolveFormat_RejectsUnsupportedFormat()
    {
        var result = DataWriterFactory.ResolveFormat("parquet", @"C:\path\file.csv");

        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Contains("Unsupported export format", result.Error.Message);
    }

    [Fact]
    public async Task DataWriterFactory_CreateWriter_ReturnsCorrectType()
    {
        using var stream1 = new MemoryStream();
        using var stream2 = new MemoryStream();

        await using var csvWriter = DataWriterFactory.CreateWriter("csv", stream1);
        await using var jsonlWriter = DataWriterFactory.CreateWriter("jsonl", stream2);

        Assert.IsType<CsvDataRowWriter>(csvWriter);
        Assert.IsType<JsonlDataRowWriter>(jsonlWriter);
    }
}
