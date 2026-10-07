using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SqlToAi.Database;

namespace SqlToAi.Cli;

internal static class SchemaExportPaths
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    internal static Dictionary<SchemaObjectIdentity, string> Create(IReadOnlyList<SchemaObject> objects)
    {
        var paths = new Dictionary<SchemaObjectIdentity, string>();
        var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "README.md" };
        foreach (SchemaObject item in objects)
        {
            string file = Escape(item.Identity.SchemaName) + "." + Escape(item.Identity.ObjectName);
            if (file.Length > 230)
            {
                int prefixLength = char.IsHighSurrogate(file[159]) ? 159 : 160;
                file = file[..prefixLength] + "~" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file)));
            }
            string path = DirectoryFor(item.Identity.Kind) + "/" + file + ".md";
            if (!planned.Add(path)) throw new ArgumentException($"Export path collision: '{path}'.");
            paths.Add(item.Identity, path);
        }
        return paths;
    }

    internal static string DirectoryFor(SchemaObjectKind kind) => kind switch
    {
        SchemaObjectKind.Table => "tables",
        SchemaObjectKind.View => "views",
        SchemaObjectKind.Procedure => "procedures",
        SchemaObjectKind.ScalarFunction or SchemaObjectKind.InlineTableValuedFunction or SchemaObjectKind.TableValuedFunction => "functions",
        SchemaObjectKind.Trigger => "triggers",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string Escape(string name)
    {
        var text = new StringBuilder();
        bool reserved = name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase) || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(name[3], StringComparison.Ordinal));
        for (int i = 0; i < name.Length; i++)
        {
            char value = name[i];
            if (InvalidFileNameChars.Contains(value) || value is '.' or '~'
                || (i == name.Length - 1 && value == ' ') || (i == 0 && reserved))
                text.Append('~').Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
            else text.Append(value);
        }
        return text.Length == 0 ? "~empty" : text.ToString();
    }
}
