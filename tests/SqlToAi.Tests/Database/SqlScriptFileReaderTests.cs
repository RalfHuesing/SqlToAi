#nullable enable

using System.Globalization;
using System.Text;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Database;

// @covers SqlToAi.Database.SqlScriptFile
// @covers SqlToAi.Database.SqlScriptFileReader
public sealed class SqlScriptFileReaderTests : IDisposable
{
    private readonly string _tempDirectory;

    public SqlScriptFileReaderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SqlToAiReaderTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_ShouldRejectEmptyPath(string? filePath)
    {
        // Act
        Result<SqlScriptFile> result = SqlScriptFileReader.Read(filePath, CreateOptions());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Contains("local SQL script file path", result.Error.Message);
    }

    [Fact]
    public void Read_ShouldResolveRelativePathAgainstCurrentDirectory()
    {
        // Arrange
        string relativePath = Path.Combine(Path.GetRelativePath(Environment.CurrentDirectory, _tempDirectory), "relative.SQL");
        string filePath = Path.Combine(_tempDirectory, "relative.SQL");
        const string expectedText = "SELECT 'relative';";
        File.WriteAllText(filePath, expectedText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // Act
        Result<SqlScriptFile> result = SqlScriptFileReader.Read(relativePath, CreateOptions());

        // Assert
        AssertSuccess(result, expectedText, Path.GetFullPath(relativePath, Environment.CurrentDirectory), "UTF-8");
    }

    [Fact]
    public void Read_ShouldAcceptAbsoluteSqlPathCaseInsensitively()
    {
        // Arrange
        string filePath = Path.Combine(_tempDirectory, "absolute.SQL");
        const string expectedText = "SELECT 'absolute';";
        File.WriteAllText(filePath, expectedText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // Act
        Result<SqlScriptFile> result = SqlScriptFileReader.Read(filePath, CreateOptions());

        // Assert
        AssertSuccess(result, expectedText, filePath, "UTF-8");
    }

    [Fact]
    public void Read_ShouldReturnFileNotFoundForMissingSqlFile()
    {
        // Act
        string filePath = Path.Combine(_tempDirectory, "missing.sql");
        Result<SqlScriptFile> result = SqlScriptFileReader.Read(filePath, CreateOptions());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(SqlToAiError.FileNotFoundCode, result.Error.Code);
        Assert.Contains(filePath, result.Error.Message);
    }

    [Fact]
    public void Read_ShouldReturnInvalidFileExtensionForNonSqlFile()
    {
        // Arrange
        string filePath = Path.Combine(_tempDirectory, "query.txt");
        File.WriteAllText(filePath, "SELECT 1;", Encoding.UTF8);

        // Act
        Result<SqlScriptFile> result = SqlScriptFileReader.Read(filePath, CreateOptions());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(SqlToAiError.InvalidFileExtensionCode, result.Error.Code);
        Assert.Contains(filePath, result.Error.Message);
    }

    [Theory]
    [InlineData("https://example.test/script.sql")]
    [InlineData("file://C:/scripts/script.sql")]
    [InlineData(@"\\server\share\script.sql")]
    public void Read_ShouldRejectNonLocalPathForms(string filePath)
    {
        // Act
        Result<SqlScriptFile> result = SqlScriptFileReader.Read(filePath, CreateOptions());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(SqlToAiError.InvalidParametersCode, result.Error.Code);
        Assert.Contains("local", result.Error.Message);
        Assert.Contains("URL or UNC", result.Error.Message);
    }

    [Fact]
    public void Read_ShouldAcceptFileAtExactSizeLimit()
    {
        // Arrange
        const long maxSizeBytes = 16;
        string filePath = Path.Combine(_tempDirectory, "at-limit.sql");
        byte[] bytes = Encoding.ASCII.GetBytes("SELECT 12345678;");
        Assert.Equal(maxSizeBytes, bytes.Length);
        File.WriteAllBytes(filePath, bytes);

        // Act
        Result<SqlScriptFile> result = SqlScriptFileReader.Read(filePath, CreateOptions(maxSizeBytes));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("SELECT 12345678;", result.Value.Text);
    }

    [Fact]
    public void Read_ShouldReturnFileTooLargeWhenOneByteOverLimit()
    {
        // Arrange
        const long maxSizeBytes = 16;
        string filePath = Path.Combine(_tempDirectory, "over-limit.sql");
        File.WriteAllBytes(filePath, Encoding.ASCII.GetBytes("SELECT 123456789;"));

        // Act
        Result<SqlScriptFile> result = SqlScriptFileReader.Read(filePath, CreateOptions(maxSizeBytes));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(SqlToAiError.FileTooLargeCode, result.Error.Code);
        Assert.Contains("17", result.Error.Message);
        Assert.Contains("16", result.Error.Message);
        Assert.Contains(filePath, result.Error.Message);
    }

    [Fact]
    public void Read_ShouldDecodeUtf8WithoutBom()
    {
        const string expectedText = "SELECT 'Grüße';";
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        AssertEncodedFile("utf8.sql", encoding, expectedText, "UTF-8");
    }

    [Fact]
    public void Read_ShouldDecodeUtf8WithBom()
    {
        const string expectedText = "SELECT 'Grüße';";
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        AssertEncodedFile("utf8-bom.sql", encoding, expectedText, "UTF-8");
    }

    [Fact]
    public void Read_ShouldDecodeUtf16LittleEndianWithBom()
    {
        const string expectedText = "SELECT 'Grüße';";
        Encoding encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);

        AssertEncodedFile("utf16-le.sql", encoding, expectedText, "UTF-16 LE");
    }

    [Fact]
    public void Read_ShouldDecodeUtf16BigEndianWithBom()
    {
        const string expectedText = "SELECT 'Grüße';";
        Encoding encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true);

        AssertEncodedFile("utf16-be.sql", encoding, expectedText, "UTF-16 BE");
    }

    [Fact]
    public void Read_ShouldFallbackToWindowsAnsiForInvalidUtf8()
    {
        // Arrange
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        int codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        Encoding encoding = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        byte[] bytes = encoding.GetBytes("Müller");
        string expectedText = encoding.GetString(bytes);
        string filePath = Path.Combine(_tempDirectory, "ansi.sql");
        File.WriteAllBytes(filePath, bytes);

        // Act
        Result<SqlScriptFile> result = SqlScriptFileReader.Read(filePath, CreateOptions());

        // Assert
        AssertSuccess(result, expectedText, filePath, "Windows-ANSI");
    }

    private static QueryExecutionOptions CreateOptions(long maxFileSizeBytes = 10_485_760L)
    {
        return new QueryExecutionOptions { MaxScriptFileSizeBytes = maxFileSizeBytes };
    }

    private void AssertEncodedFile(string fileName, Encoding encoding, string expectedText, string expectedEncodingName)
    {
        string filePath = Path.Combine(_tempDirectory, fileName);
        byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes(expectedText)).ToArray();
        File.WriteAllBytes(filePath, bytes);

        Result<SqlScriptFile> result = SqlScriptFileReader.Read(filePath, CreateOptions());

        AssertSuccess(result, expectedText, filePath, expectedEncodingName);
    }

    private static void AssertSuccess(Result<SqlScriptFile> result, string expectedText, string expectedPath, string expectedEncodingName)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "Expected a successful SQL script file result.");
        Assert.Equal(expectedPath, result.Value.ResolvedPath);
        Assert.Equal(expectedText, result.Value.Text);
        Assert.Equal(expectedEncodingName, result.Value.EncodingName);
    }
}
