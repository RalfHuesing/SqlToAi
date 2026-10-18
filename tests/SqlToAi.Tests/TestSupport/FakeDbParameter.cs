using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace SqlToAi.Tests.TestSupport;

/// <summary>
/// Generic <see cref="DbParameter"/> fake shared by every ADO.NET test double in this project.
/// Stores parameter values, names and explicit types for command assertions. None of the
/// members carry test-specific behavior, so a single
/// implementation covers every mock connection/command in <c>tests/SqlToAi.Tests</c>.
/// </summary>
internal sealed class FakeDbParameter : DbParameter
{
    [AllowNull]
    public override string ParameterName { get; set; } = string.Empty;

    [AllowNull]
    public override object Value { get; set; } = DBNull.Value;

    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override int Size { get; set; }

    [AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;

    public override bool SourceColumnNullMapping { get; set; }
    public override void ResetDbType() { }
}
