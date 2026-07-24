#nullable enable

using System.Collections;
using System.Data.Common;

namespace SqlToAi.Tests.TestSupport;

/// <summary>
/// Generic <see cref="DbParameterCollection"/> fake shared by every ADO.NET test double in this
/// project. Backed by a plain <see cref="List{DbParameter}"/> — every member is a byte-for-byte
/// mechanical forwarding call, so a single fully-functional implementation is safe to reuse
/// everywhere: production code (via Dapper) only ever calls <see cref="Add"/>/<see cref="Clear"/>
/// and enumerates the collection, never the by-name lookups/removals, so there is no
/// test-specific behavior to preserve per call site.
/// </summary>
internal sealed class FakeDbParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = [];

    public override int Count => _parameters.Count;
    public override object SyncRoot => this;
    public override bool IsReadOnly => false;
    public override bool IsFixedSize => false;

    public override int Add(object value)
    {
        _parameters.Add((DbParameter)value);
        return _parameters.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (object? value in values)
        {
            Add(value!);
        }
    }

    public override void Clear() => _parameters.Clear();

    public override bool Contains(object value) => _parameters.Contains((DbParameter)value);

    public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);

    public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);

    public override void Remove(object value) => _parameters.Remove((DbParameter)value);

    public override void RemoveAt(int index) => _parameters.RemoveAt(index);

    protected override DbParameter GetParameter(int index) => _parameters[index];

    protected override DbParameter GetParameter(string parameterName) =>
        _parameters.FirstOrDefault(p => p.ParameterName == parameterName)
        ?? throw new KeyNotFoundException(parameterName);

    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

    protected override void SetParameter(string parameterName, DbParameter value)
    {
        int index = _parameters.FindIndex(p => p.ParameterName == parameterName);
        if (index >= 0)
        {
            _parameters[index] = value;
        }
        else
        {
            _parameters.Add(value);
        }
    }

    public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);

    public override int IndexOf(string parameterName) => _parameters.FindIndex(p => p.ParameterName == parameterName);

    public override void RemoveAt(string parameterName)
    {
        int index = _parameters.FindIndex(p => p.ParameterName == parameterName);
        if (index >= 0)
        {
            _parameters.RemoveAt(index);
        }
    }

    public override void CopyTo(Array array, int index) => ((ICollection)_parameters).CopyTo(array, index);

    public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();
}
