using System.Text;

namespace SqlToAi.Database;

internal static class OfflineSqlNameFormatter
{
    internal static string Text(string name)
    {
        var result = new StringBuilder();
        foreach (char character in Display(name))
        {
            // Pipe entities also work in the shared pipe-table renderer without double escaping.
            if (character == '|') result.Append("&#124;");
            else
            {
                if ("\\[]`*_<>&#!~".Contains(character)) result.Append('\\');
                result.Append(character);
            }
        }
        return result.ToString();
    }

    internal static string Code(string name)
    {
        string display = Display(name);
        int longestRun = 0, run = 0;
        foreach (char character in display)
        {
            run = character == '`' ? run + 1 : 0;
            longestRun = Math.Max(longestRun, run);
        }
        string delimiter = new('`', longestRun + 1);
        bool padding = display.StartsWith('`') || display.EndsWith('`')
            || (display.StartsWith(' ') && display.EndsWith(' ') && display.Any(character => character != ' '));
        return padding ? $"{delimiter} {display} {delimiter}" : $"{delimiter}{display}{delimiter}";
    }

    private static string Display(string name)
    {
        var result = new StringBuilder();
        foreach (char character in name)
        {
            // Keep line endings visible and distinguish them from literal SQL-name backslashes.
            result.Append(character switch
            {
                '\\' => "\\\\",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ when char.IsControl(character) => $"\\u{(int)character:X4}",
                _ => character.ToString()
            });
        }
        return result.ToString();
    }
}
