#nullable enable

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;

namespace SqlToAi.Database;

/// <summary>
/// Binds SQL parameters from dynamic JSON inputs or dictionaries to ADO.NET <see cref="DbCommand"/> objects.
/// Supports automatic type detection (primitives, ISO-8601 dates, Guids) as well as explicit type overrides
/// via structured JSON parameter objects (e.g. <c>{"value": "123", "dbType": "AnsiString"}</c>).
/// </summary>
public static class SqlParameterBinder
{
    /// <summary>
    /// Binds the given parameters object or JSON structure to the target <see cref="DbCommand"/>.
    /// </summary>
    /// <param name="command">The target database command.</param>
    /// <param name="rawParameters">
    /// The parameters object, which can be an <see cref="IDictionary{TKey, TValue}"/>,
    /// a <see cref="JsonElement"/> representing a JSON object, or a JSON string.
    /// </param>
    public static void BindParameters(DbCommand command, object? rawParameters)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (rawParameters == null)
        {
            return;
        }

        if (rawParameters is JsonElement jsonElement)
        {
            BindJsonElement(command, jsonElement);
            return;
        }

        if (rawParameters is string jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                return;
            }

            using var doc = JsonDocument.Parse(jsonString);
            BindJsonElement(command, doc.RootElement);
            return;
        }

        if (rawParameters is IDictionary<string, object?> dict)
        {
            BindDictionary(command, dict);
            return;
        }

        throw new ArgumentException($"Unsupported parameter container type: '{rawParameters.GetType().FullName}'.");
    }

    private static void BindJsonElement(DbCommand command, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            string rawStr = element.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawStr))
            {
                return;
            }
            using var doc = JsonDocument.Parse(rawStr);
            BindJsonElement(command, doc.RootElement);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Parameters argument must be a JSON object.");
        }

        foreach (var property in element.EnumerateObject())
        {
            AddParameter(command, property.Name, property.Value);
        }
    }

    private static void BindDictionary(DbCommand command, IDictionary<string, object?> dict)
    {
        foreach (var (key, value) in dict)
        {
            if (value is JsonElement jsonEl)
            {
                AddParameter(command, key, jsonEl);
            }
            else
            {
                AddParameterFromValue(command, key, value, null);
            }
        }
    }

    private static void AddParameter(DbCommand command, string paramName, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object && TryExtractExplicitTypeObject(element, out var explicitValue, out var dbType))
        {
            AddParameterFromValue(command, paramName, explicitValue, dbType);
            return;
        }

        var (inferredValue, inferredDbType) = ParseJsonValue(element);
        AddParameterFromValue(command, paramName, inferredValue, inferredDbType);
    }

    private static bool TryExtractExplicitTypeObject(JsonElement element, out object? explicitValue, out DbType? dbType)
    {
        explicitValue = null;
        dbType = null;

        bool hasValueProp = false;
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.NameEquals("value") || prop.NameEquals("Value"))
            {
                hasValueProp = true;
                var (val, inferredType) = ParseJsonValue(prop.Value);
                explicitValue = val;
                if (dbType == null)
                {
                    dbType = inferredType;
                }
            }
            else if (prop.NameEquals("dbType") || prop.NameEquals("DbType") || prop.NameEquals("sqlDbType") || prop.NameEquals("SqlDbType"))
            {
                string? typeStr = prop.Value.GetString();
                if (!string.IsNullOrWhiteSpace(typeStr) && Enum.TryParse<DbType>(typeStr, ignoreCase: true, out var parsedDbType))
                {
                    dbType = parsedDbType;
                }
            }
        }

        return hasValueProp;
    }

    private static (object? Value, DbType? DbType) ParseJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => (DBNull.Value, null),
            JsonValueKind.True => (true, DbType.Boolean),
            JsonValueKind.False => (false, DbType.Boolean),
            JsonValueKind.Number => ParseJsonNumber(element),
            JsonValueKind.String => ParseJsonString(element.GetString() ?? string.Empty),
            _ => (element.GetRawText(), DbType.String)
        };
    }

    private static (object Value, DbType DbType) ParseJsonNumber(JsonElement element)
    {
        if (element.TryGetInt32(out int iVal))
        {
            return (iVal, DbType.Int32);
        }
        if (element.TryGetInt64(out long lVal))
        {
            return (lVal, DbType.Int64);
        }
        if (element.TryGetDecimal(out decimal decVal))
        {
            return (decVal, DbType.Decimal);
        }
        return (element.GetDouble(), DbType.Double);
    }

    private static (object Value, DbType DbType) ParseJsonString(string strValue)
    {
        if (DateTime.TryParse(strValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            return (dt, DbType.DateTime);
        }

        if (Guid.TryParse(strValue, out var guid))
        {
            return (guid, DbType.Guid);
        }

        return (strValue, DbType.String);
    }

    private static void AddParameterFromValue(DbCommand command, string paramName, object? value, DbType? dbType)
    {
        string normalizedName = paramName.StartsWith('@') ? paramName : "@" + paramName;

        if (command.Parameters.Contains(normalizedName))
        {
            var existingParam = command.Parameters[normalizedName];
            existingParam.Value = value ?? DBNull.Value;
            if (dbType.HasValue)
            {
                existingParam.DbType = dbType.Value;
            }
            return;
        }

        var param = command.CreateParameter();
        param.ParameterName = normalizedName;
        param.Value = value ?? DBNull.Value;

        if (dbType.HasValue)
        {
            param.DbType = dbType.Value;
        }

        command.Parameters.Add(param);
    }
}
