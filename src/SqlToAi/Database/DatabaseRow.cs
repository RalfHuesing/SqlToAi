using System.Data.Common;
using System.Globalization;

namespace SqlToAi.Database;

// Explicit query mappers use SQL aliases; absent and NULL fields retain their declared defaults.
internal sealed class DatabaseRow
{
    private readonly DbDataReader _reader;
    private readonly Dictionary<string, int> _ordinals = new(StringComparer.OrdinalIgnoreCase);

    internal DatabaseRow(DbDataReader reader)
    {
        _reader = reader;
        for (int ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            _ordinals.TryAdd(reader.GetName(ordinal), ordinal);
        }
    }

    internal string GetString(string name, string defaultValue = "") => GetStringOrNull(name) ?? defaultValue;
    internal string GetString(int ordinal) => GetStringOrNull(ordinal) ?? string.Empty;
    internal string? GetStringOrNull(string name) => GetValue(name)?.ToString();
    internal string? GetStringOrNull(int ordinal) => GetValue(ordinal)?.ToString();
    internal int GetInt32(string name) => Convert.ToInt32(GetValue(name), CultureInfo.InvariantCulture);
    internal int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal), CultureInfo.InvariantCulture);
    internal long GetInt64(string name) => Convert.ToInt64(GetValue(name), CultureInfo.InvariantCulture);
    internal long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal), CultureInfo.InvariantCulture);
    internal double GetDouble(string name) => Convert.ToDouble(GetValue(name), CultureInfo.InvariantCulture);
    internal bool GetBoolean(string name) => Convert.ToBoolean(GetValue(name), CultureInfo.InvariantCulture);
    internal bool GetBoolean(int ordinal) => Convert.ToBoolean(GetValue(ordinal), CultureInfo.InvariantCulture);
    internal int? GetNullableInt32(string name) => GetValue(name) is { } value ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : null;
    internal int? GetNullableInt32(int ordinal) => GetValue(ordinal) is { } value ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : null;
    internal DateTime? GetNullableDateTime(string name) => GetValue(name) is { } value ? Convert.ToDateTime(value, CultureInfo.InvariantCulture) : null;

    private object? GetValue(string name) => _ordinals.TryGetValue(name, out int ordinal) ? GetValue(ordinal) : null;
    private object? GetValue(int ordinal) => _reader.IsDBNull(ordinal) ? null : _reader.GetValue(ordinal);
}
