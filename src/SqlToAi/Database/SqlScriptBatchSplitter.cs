#nullable enable

using System.Collections.Generic;
using System.Text;

namespace SqlToAi.Database;

internal static class SqlScriptBatchSplitter
{
    public static IReadOnlyList<SqlBatch> Split(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return [];
        }

        var batches = new List<SqlBatch>();
        var batchText = new StringBuilder();
        var state = new ScanState(ScanMode.Normal, 0);
        int startLine = 0;
        int endLine = 0;

        foreach (var line in EnumerateLines(script))
        {
            if (state.IsNormal && TryParseSeparator(line.Text, out int repeatCount))
            {
                AddBatch(batches, batchText, new BatchMetadata(startLine, endLine, repeatCount));
                batchText.Clear();
                startLine = 0;
                endLine = 0;
                continue;
            }

            if (startLine == 0)
            {
                startLine = line.Number;
            }

            endLine = line.Number;
            batchText.Append(line.Text).Append(line.Terminator);
            state = AdvanceState(line.Text, state);
        }

        AddBatch(batches, batchText, new BatchMetadata(startLine, endLine, 1));
        return batches;
    }

    private static void AddBatch(
        List<SqlBatch> batches,
        StringBuilder batchText,
        BatchMetadata metadata)
    {
        string text = batchText.ToString();
        if (metadata.StartLine == 0 || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        batches.Add(new SqlBatch(text, metadata.StartLine, metadata.EndLine, metadata.RepeatCount));
    }

    private static IEnumerable<SqlLine> EnumerateLines(string script)
    {
        int lineStart = 0;
        int lineNumber = 1;

        for (int index = 0; index < script.Length; index++)
        {
            if (script[index] != '\n')
            {
                continue;
            }

            int contentEnd = index;
            string terminator = "\n";
            if (contentEnd > lineStart && script[contentEnd - 1] == '\r')
            {
                contentEnd--;
                terminator = "\r\n";
            }

            yield return new SqlLine(script[lineStart..contentEnd], terminator, lineNumber);
            lineStart = index + 1;
            lineNumber++;
        }

        if (lineStart < script.Length)
        {
            yield return new SqlLine(script[lineStart..], string.Empty, lineNumber);
        }
    }

    private static bool TryParseSeparator(string line, out int repeatCount)
    {
        repeatCount = 1;
        int index = SkipWhitespace(line, 0);
        if (!IsGoToken(line, index))
        {
            return false;
        }

        int afterGo = index + 2;
        if (afterGo < line.Length
            && !char.IsWhiteSpace(line[afterGo])
            && !StartsComment(line, afterGo))
        {
            return false;
        }

        index = SkipWhitespace(line, afterGo);
        if (index < line.Length && IsAsciiDigit(line[index]))
        {
            int countStart = index;
            while (index < line.Length && IsAsciiDigit(line[index]))
            {
                index++;
            }

            if (!TryParsePositiveCount(line, countStart, index, out repeatCount))
            {
                return false;
            }
        }

        return TryReadTrailingComments(line, ref index);
    }

    private static bool TryParsePositiveCount(string line, int start, int end, out int count)
    {
        count = 0;
        for (int index = start; index < end; index++)
        {
            int digit = line[index] - '0';
            if (count > (int.MaxValue - digit) / 10)
            {
                return false;
            }

            count = count * 10 + digit;
        }

        return count > 0;
    }

    private static bool TryReadTrailingComments(string line, ref int index)
    {
        int blockCommentDepth = 0;
        while (index < line.Length)
        {
            if (blockCommentDepth == 0)
            {
                index = SkipWhitespace(line, index);
                if (index >= line.Length || StartsLineComment(line, index))
                {
                    return true;
                }

                if (!StartsBlockComment(line, index))
                {
                    return false;
                }

                blockCommentDepth = 1;
                index += 2;
                continue;
            }

            if (StartsBlockComment(line, index))
            {
                blockCommentDepth++;
                index += 2;
                continue;
            }

            if (IsPair(line, index, '*', '/'))
            {
                blockCommentDepth--;
                index += 2;
                continue;
            }

            index++;
        }

        return blockCommentDepth == 0;
    }

    private static int SkipWhitespace(string line, int start)
    {
        int index = start;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsGoToken(string line, int index)
    {
        return index + 1 < line.Length
            && (line[index] == 'G' || line[index] == 'g')
            && (line[index + 1] == 'O' || line[index + 1] == 'o');
    }

    private static bool StartsComment(string line, int index)
    {
        return StartsLineComment(line, index) || StartsBlockComment(line, index);
    }

    private static bool StartsLineComment(string line, int index)
    {
        return index + 1 < line.Length && line[index] == '-' && line[index + 1] == '-';
    }

    private static bool StartsBlockComment(string line, int index)
    {
        return index + 1 < line.Length && line[index] == '/' && line[index + 1] == '*';
    }

    private static bool IsAsciiDigit(char value)
    {
        return value is >= '0' and <= '9';
    }

    private static ScanState AdvanceState(string line, ScanState state)
    {
        for (int index = 0; index < line.Length; index++)
        {
            if (TryAdvanceSpecialState(line, ref state, ref index))
            {
                continue;
            }

            if (StartsLineComment(line, index))
            {
                break;
            }

            state = AdvanceNormalState(line, state, ref index);
        }

        return state;
    }

    private static bool TryAdvanceSpecialState(string line, ref ScanState state, ref int index)
    {
        if (state.BlockCommentDepth > 0)
        {
            AdvanceBlockComment(line, ref state, ref index);
            return true;
        }

        switch (state.Mode)
        {
            case ScanMode.SingleQuote:
                AdvanceSingleQuote(line, ref state, ref index);
                return true;
            case ScanMode.BracketIdentifier:
                AdvanceBracketIdentifier(line, ref state, ref index);
                return true;
            case ScanMode.DoubleQuote:
                AdvanceDoubleQuote(line, ref state, ref index);
                return true;
            default:
                return false;
        }
    }

    private static void AdvanceBlockComment(string line, ref ScanState state, ref int index)
    {
        if (StartsBlockComment(line, index))
        {
            state = state with { BlockCommentDepth = state.BlockCommentDepth + 1 };
            index++;
        }
        else if (IsPair(line, index, '*', '/'))
        {
            state = state with { BlockCommentDepth = state.BlockCommentDepth - 1 };
            index++;
        }
    }

    private static void AdvanceSingleQuote(string line, ref ScanState state, ref int index)
    {
        if (IsPair(line, index, '\'', '\''))
        {
            index++;
        }
        else if (line[index] == '\'')
        {
            state = state with { Mode = ScanMode.Normal };
        }
    }

    private static void AdvanceBracketIdentifier(string line, ref ScanState state, ref int index)
    {
        if (IsPair(line, index, ']', ']'))
        {
            index++;
        }
        else if (line[index] == ']')
        {
            state = state with { Mode = ScanMode.Normal };
        }
    }

    private static void AdvanceDoubleQuote(string line, ref ScanState state, ref int index)
    {
        if (IsPair(line, index, '"', '"'))
        {
            index++;
        }
        else if (line[index] == '"')
        {
            state = state with { Mode = ScanMode.Normal };
        }
    }

    private static ScanState AdvanceNormalState(string line, ScanState state, ref int index)
    {
        if (StartsBlockComment(line, index))
        {
            index++;
            return state with { BlockCommentDepth = 1 };
        }

        return line[index] switch
        {
            '\'' => state with { Mode = ScanMode.SingleQuote },
            '[' => state with { Mode = ScanMode.BracketIdentifier },
            '"' => state with { Mode = ScanMode.DoubleQuote },
            _ => state
        };
    }

    private static bool IsPair(string line, int index, char first, char second)
    {
        return index + 1 < line.Length && line[index] == first && line[index + 1] == second;
    }

    private enum ScanMode
    {
        Normal,
        SingleQuote,
        BracketIdentifier,
        DoubleQuote
    }

    private readonly record struct ScanState(ScanMode Mode, int BlockCommentDepth)
    {
        public bool IsNormal => Mode == ScanMode.Normal && BlockCommentDepth == 0;
    }

    private readonly record struct BatchMetadata(int StartLine, int EndLine, int RepeatCount);

    private readonly record struct SqlLine(string Text, string Terminator, int Number);
}
