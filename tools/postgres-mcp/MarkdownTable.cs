// Markdown table renderer: escapes pipes/newlines, truncates long cells so
// a wide row cannot blow the agent's context window (C18 resource limits).

using System.Globalization;
using System.Text;

namespace SBQR.PostgresMcp;

internal static class MarkdownTable
{
    public const int MaxCellChars = 2000;

    public static string Render(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string?>> rows,
        int totalRows,
        int rowCap,
        string? note = null)
    {
        var sb = new StringBuilder();
        sb.Append("| ").AppendJoin(" | ", columns.Select(Escape)).AppendLine(" |");
        sb.Append("| ").AppendJoin(" | ", columns.Select(_ => "---")).AppendLine(" |");
        foreach (var row in rows)
        {
            sb.Append("| ").AppendJoin(" | ", row.Select(FormatCell)).AppendLine(" |");
        }

        if (totalRows > rows.Count)
        {
            sb.AppendLine().Append(CultureInfo.InvariantCulture,
                $"_{totalRows} row(s) matched; showing first {rows.Count} (cap {rowCap})._");
        }
        else
        {
            sb.AppendLine().Append(CultureInfo.InvariantCulture, $"_{totalRows} row(s)._");
        }

        if (!string.IsNullOrWhiteSpace(note))
        {
            sb.Append(' ').Append(note);
        }

        return sb.ToString();
    }

    private static string FormatCell(string? value)
    {
        if (value is null)
        {
            return "*NULL*";
        }

        if (value.Length > MaxCellChars)
        {
            return Escape(value[..MaxCellChars]) + $"… *(+{value.Length - MaxCellChars} chars)*";
        }

        return value.Length == 0 ? "*empty*" : Escape(value);
    }

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal)
            .Replace("\r", "<br>", StringComparison.Ordinal);
}
