#nullable enable

using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace SqlToAi.Tests.TestSupport;

/// <summary>
/// Schema-table origin (<c>BaseTableName</c>/<c>BaseColumnName</c>/<c>BaseSchemaName</c>) that
/// <see cref="FakeDbDataReader.GetSchemaTable"/> reports for column 0, letting tests exercise
/// providers that expose column-origin metadata (see
/// <c>QueryExecutionService.GetColumnOrigins</c>). Only column 0 is modeled because every current
/// caller uses single-origin, single-assertion readers; extend if a future test needs more.
/// </summary>
internal sealed record FakeSchemaTableOrigin(string? BaseTableName = null, string? BaseColumnName = null, string? BaseSchemaName = null);

/// <summary>
/// Represents a single result set table within a <see cref="FakeDbDataReader"/>.
/// </summary>
internal sealed record FakeDbResultSet(string[] Columns, IReadOnlyList<object?[]> Rows, FakeSchemaTableOrigin? Origin = null);

/// <summary>
/// Generic, table-based <see cref="DbDataReader"/> fake shared by every ADO.NET test double in
/// this project. Configured with one or more result sets; typed getters cast the stored value for
/// the requested ordinal, and <see cref="GetFieldType"/> infers each column's CLR type from the
/// first non-null value found in that column.
/// </summary>
internal sealed class FakeDbDataReader : DbDataReader
{
    private readonly IReadOnlyList<FakeDbResultSet> _resultSets;
    private int _resultSetIndex;
    private int _index = -1;

    public FakeDbDataReader(string[] columns, IReadOnlyList<object?[]> rows, FakeSchemaTableOrigin? origin = null)
        : this([new FakeDbResultSet(columns, rows, origin)])
    {
    }

    public FakeDbDataReader(IReadOnlyList<FakeDbResultSet> resultSets)
    {
        _resultSets = resultSets.Count > 0 ? resultSets : [new FakeDbResultSet([], [])];
    }

    private FakeDbResultSet CurrentSet => _resultSets[_resultSetIndex];

    public override int FieldCount => CurrentSet.Columns.Length;
    public override int Depth => 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override bool HasRows => CurrentSet.Rows.Count > 0;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (_index < CurrentSet.Rows.Count - 1)
        {
            _index++;
            return true;
        }
        return false;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());

    public override bool NextResult()
    {
        if (_resultSetIndex < _resultSets.Count - 1)
        {
            _resultSetIndex++;
            _index = -1;
            return true;
        }
        return false;
    }

    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(NextResult());

    public override string GetName(int ordinal) => CurrentSet.Columns[ordinal];

    public override int GetOrdinal(string name)
    {
        int index = Array.FindIndex(CurrentSet.Columns, c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            return index;
        }
#pragma warning disable CA2201 // Do not throw reserved exception types — mirrors the real ADO.NET contract for an unknown column name.
        throw new IndexOutOfRangeException(name);
#pragma warning restore CA2201
    }

    public override object GetValue(int ordinal)
    {
        if (_index < 0 || _index >= CurrentSet.Rows.Count)
        {
            return DBNull.Value;
        }
        return CurrentSet.Rows[_index][ordinal] ?? DBNull.Value;
    }

    public override Type GetFieldType(int ordinal)
    {
        foreach (object?[] row in CurrentSet.Rows)
        {
            if (ordinal < row.Length && row[ordinal] is { } value and not DBNull)
            {
                return value.GetType();
            }
        }
        return typeof(string);
    }

    public override DataTable? GetSchemaTable()
    {
        if (CurrentSet.Origin is null)
        {
            return null;
        }

        var table = new DataTable();
        table.Columns.Add("ColumnOrdinal", typeof(int));
        table.Columns.Add("BaseTableName", typeof(string));
        table.Columns.Add("BaseColumnName", typeof(string));
        table.Columns.Add("BaseSchemaName", typeof(string));
        DataRow row = table.NewRow();
        row["ColumnOrdinal"] = 0;
        row["BaseTableName"] = (object?)CurrentSet.Origin.BaseTableName ?? DBNull.Value;
        row["BaseColumnName"] = (object?)CurrentSet.Origin.BaseColumnName ?? DBNull.Value;
        row["BaseSchemaName"] = (object?)CurrentSet.Origin.BaseSchemaName ?? DBNull.Value;
        table.Rows.Add(row);
        return table;
    }

    public override bool IsDBNull(int ordinal) => GetValue(ordinal) == DBNull.Value;

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override string GetDataTypeName(int ordinal) => "varchar";
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal), CultureInfo.InvariantCulture);
    public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal), CultureInfo.InvariantCulture);
    public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal), CultureInfo.InvariantCulture);
    public override string GetString(int ordinal) => GetValue(ordinal)?.ToString() ?? string.Empty;

    public override int GetValues(object[] values)
    {
        int count = Math.Min(FieldCount, values.Length);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }
        return count;
    }

    public override IEnumerator GetEnumerator() => CurrentSet.Rows.GetEnumerator();
}
