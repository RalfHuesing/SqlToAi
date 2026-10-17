using SqlToAi.Domain;

namespace SqlToAi.Database;

/// <summary>Parses the explicit alias/database target syntax without interpreting database names.</summary>
public static class DatabaseTargetParser
{
    public static Result<ParsedDatabaseTarget> Parse(string? target)
    {
        if (target is null)
        {
            return SqlToAiError.InvalidParameters("database target is required in alias/database form.");
        }

        int separator = target.IndexOf('/');
        if (separator <= 0 || separator == target.Length - 1)
        {
            return SqlToAiError.InvalidParameters("database target must use alias/database form with non-empty parts.");
        }

        ReadOnlySpan<char> alias = target.AsSpan(0, separator);
        if (!IsValidAlias(alias))
        {
            return SqlToAiError.InvalidParameters("database target contains a malformed server alias.");
        }

        string canonicalAlias = CanonicalizeAlias(alias);
        string databaseName = target[(separator + 1)..];
        return new ParsedDatabaseTarget(canonicalAlias, databaseName);
    }

    public static bool IsValidAlias(string? alias) =>
        alias is not null && IsValidAlias(alias.AsSpan());

    public static string CanonicalizeAlias(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        if (!IsValidAlias(alias.AsSpan()))
        {
            throw new ArgumentException("Server aliases must match [A-Za-z][A-Za-z0-9_-]*.", nameof(alias));
        }

        return CanonicalizeAlias(alias.AsSpan());
    }

    private static bool IsValidAlias(ReadOnlySpan<char> alias)
    {
        if (alias.IsEmpty || !IsAsciiLetter(alias[0]))
        {
            return false;
        }

        for (int i = 1; i < alias.Length; i++)
        {
            char character = alias[i];
            if (!IsAsciiLetter(character) && !char.IsAsciiDigit(character) && character is not '_' and not '-')
            {
                return false;
            }
        }

        return true;
    }

    private static string CanonicalizeAlias(ReadOnlySpan<char> alias)
    {
        return string.Create(alias.Length, alias.ToString(), static (destination, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                char character = source[i];
                destination[i] = character is >= 'A' and <= 'Z' ? (char)(character + ('a' - 'A')) : character;
            }
        });
    }

    private static bool IsAsciiLetter(char character) => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
