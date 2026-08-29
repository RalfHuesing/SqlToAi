#nullable enable

using System.Globalization;
using System.Text;
using SqlToAi.Configuration;
using SqlToAi.Domain;

namespace SqlToAi.Database;

internal static class SqlScriptFileReader
{
    private const string SqlExtension = ".sql";
    private const string Utf8EncodingName = "UTF-8";
    private const string Utf16LittleEndianEncodingName = "UTF-16 LE";
    private const string Utf16BigEndianEncodingName = "UTF-16 BE";
    private const string WindowsAnsiEncodingName = "Windows-ANSI";

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf16LittleEndian = new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);
    private static readonly Encoding Utf16BigEndian = new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true);
    private static readonly byte[] Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble();
    private static readonly byte[] Utf16LittleEndianBom = Utf16LittleEndian.GetPreamble();
    private static readonly byte[] Utf16BigEndianBom = Utf16BigEndian.GetPreamble();
    private static readonly byte[] Utf32LittleEndianBom = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] Utf32BigEndianBom = [0x00, 0x00, 0xFE, 0xFF];

    static SqlScriptFileReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Result<SqlScriptFile> Read(string? filePath, QueryExecutionOptions options)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return SqlToAiError.InvalidParameters("A local SQL script file path is required.");
        }

        if (options is null)
        {
            return SqlToAiError.InvalidParameters("Query execution options are required.");
        }

        Result<string> pathResult = ResolvePath(filePath);
        if (pathResult.IsFailure)
        {
            return pathResult.Error;
        }

        string resolvedPath = pathResult.Value;
        if (!HasSqlExtension(resolvedPath))
        {
            return SqlToAiError.InvalidFileExtension(resolvedPath);
        }

        Result<byte[]> bytesResult = ReadBytes(resolvedPath, options.MaxScriptFileSizeBytes);
        if (bytesResult.IsFailure)
        {
            return bytesResult.Error;
        }

        Result<DecodedContent> contentResult = DecodeContent(bytesResult.Value, resolvedPath);
        if (contentResult.IsFailure)
        {
            return contentResult.Error;
        }

        DecodedContent content = contentResult.Value;
        return new SqlScriptFile(resolvedPath, content.Text, content.EncodingName);
    }

    private static Result<string> ResolvePath(string filePath)
    {
        if (IsNonLocalPath(filePath))
        {
            return SqlToAiError.InvalidParameters($"The script path must be local and not a URL or UNC path: '{filePath}'.");
        }

        try
        {
            return Path.GetFullPath(filePath, Environment.CurrentDirectory);
        }
        catch (ArgumentException exception)
        {
            return SqlToAiError.InvalidParameters($"The script path '{filePath}' is invalid: {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            return SqlToAiError.InvalidParameters($"The script path '{filePath}' is not supported: {exception.Message}");
        }
        catch (PathTooLongException exception)
        {
            return SqlToAiError.InvalidParameters($"The script path '{filePath}' is too long: {exception.Message}");
        }
    }

    private static bool IsNonLocalPath(string filePath)
    {
        if (filePath.StartsWith(@"\\", StringComparison.Ordinal)
            || filePath.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        return Uri.TryCreate(filePath, UriKind.Absolute, out Uri? uri)
            && uri.IsAbsoluteUri
            && !IsWindowsDrivePath(filePath);
    }

    private static bool IsWindowsDrivePath(string filePath)
    {
        return filePath.Length >= 2
            && char.IsAsciiLetter(filePath[0])
            && filePath[1] == ':';
    }

    private static bool HasSqlExtension(string filePath)
    {
        return string.Equals(Path.GetExtension(filePath), SqlExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static Result<byte[]> ReadBytes(string filePath, long maxFileSizeBytes)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan);
            long fileSizeBytes = stream.Length;
            if (fileSizeBytes > maxFileSizeBytes)
            {
                return SqlToAiError.FileTooLarge(filePath, fileSizeBytes, maxFileSizeBytes);
            }

            if (fileSizeBytes > int.MaxValue)
            {
                return SqlToAiError.InfrastructureError($"The SQL script file '{filePath}' cannot be loaded because its size exceeds the supported in-memory limit.");
            }

            byte[] bytes = new byte[(int)fileSizeBytes];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (FileNotFoundException)
        {
            return SqlToAiError.FileNotFound(filePath);
        }
        catch (DirectoryNotFoundException)
        {
            return SqlToAiError.FileNotFound(filePath);
        }
        catch (UnauthorizedAccessException exception)
        {
            return SqlToAiError.InfrastructureError($"Could not read SQL script file '{filePath}': {exception.Message}");
        }
        catch (IOException exception)
        {
            return SqlToAiError.InfrastructureError($"Could not read SQL script file '{filePath}': {exception.Message}");
        }
    }

    private static Result<DecodedContent> DecodeContent(byte[] bytes, string filePath)
    {
        if (HasPrefix(bytes, Utf32LittleEndianBom) || HasPrefix(bytes, Utf32BigEndianBom))
        {
            return SqlToAiError.InfrastructureError($"The SQL script file '{filePath}' uses an unsupported UTF-32 byte-order mark.");
        }

        if (HasPrefix(bytes, Utf8Bom))
        {
            return DecodeWithEncoding(bytes, Utf8Bom.Length, StrictUtf8, Utf8EncodingName, filePath);
        }

        if (HasPrefix(bytes, Utf16LittleEndianBom))
        {
            return DecodeWithEncoding(bytes, Utf16LittleEndianBom.Length, Utf16LittleEndian, Utf16LittleEndianEncodingName, filePath);
        }

        if (HasPrefix(bytes, Utf16BigEndianBom))
        {
            return DecodeWithEncoding(bytes, Utf16BigEndianBom.Length, Utf16BigEndian, Utf16BigEndianEncodingName, filePath);
        }

        return DecodeBomlessContent(bytes, filePath);
    }

    private static Result<DecodedContent> DecodeBomlessContent(byte[] bytes, string filePath)
    {
        try
        {
            return new DecodedContent(StrictUtf8.GetString(bytes), Utf8EncodingName);
        }
        catch (DecoderFallbackException)
        {
            return DecodeAsWindowsAnsi(bytes, filePath);
        }
    }

    private static Result<DecodedContent> DecodeAsWindowsAnsi(byte[] bytes, string filePath)
    {
        try
        {
            int codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            Encoding windowsAnsi = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            return new DecodedContent(windowsAnsi.GetString(bytes), WindowsAnsiEncodingName);
        }
        catch (DecoderFallbackException exception)
        {
            return SqlToAiError.InfrastructureError($"Could not decode SQL script file '{filePath}' as UTF-8 or Windows ANSI: {exception.Message}");
        }
        catch (ArgumentException exception)
        {
            return SqlToAiError.InfrastructureError($"Could not load the Windows ANSI encoding for SQL script file '{filePath}': {exception.Message}");
        }
    }

    private static Result<DecodedContent> DecodeWithEncoding(
        byte[] bytes,
        int contentOffset,
        Encoding encoding,
        string encodingName,
        string filePath)
    {
        try
        {
            string text = encoding.GetString(bytes, contentOffset, bytes.Length - contentOffset);
            return new DecodedContent(text, encodingName);
        }
        catch (DecoderFallbackException exception)
        {
            return SqlToAiError.InfrastructureError($"Could not decode SQL script file '{filePath}' as {encodingName}: {exception.Message}");
        }
    }

    private static bool HasPrefix(byte[] bytes, byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        return bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);
    }

    private readonly record struct DecodedContent(string Text, string EncodingName);
}
