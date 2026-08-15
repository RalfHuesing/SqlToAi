#nullable enable

namespace SqlToAi.Database;

/// <summary>
/// Canonical values of <c>sys.objects.type</c> used by the schema renderers to
/// distinguish tables from views, procedures, and functions.
/// </summary>
internal static class SqlServerObjectType
{
    public const string UserTable = "U";
    public const string View = "V";
}
