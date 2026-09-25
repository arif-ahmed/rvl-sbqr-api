// Read-only statement guard for pg_query. This is defense in depth: the
// query still runs inside a server-enforced READ ONLY transaction, so a
// write that slipped past this parser would fail at the database anyway.

namespace SBQR.PostgresMcp;

internal static class SqlGuard
{
    private static readonly HashSet<string> ReadFirstWords =
        new(["SELECT", "WITH", "VALUES", "TABLE", "EXPLAIN"], StringComparer.OrdinalIgnoreCase);

    public static void EnsureReadOnly(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("SQL must not be empty.");
        }

        var body = StripStringsAndComments(sql);
        EnsureSingleStatement(body);

        var firstWord = FirstWord(body);
        if (!ReadFirstWords.Contains(firstWord))
        {
            throw new ArgumentException(
                $"pg_query accepts read-only statements only (SELECT/WITH/VALUES/TABLE/EXPLAIN); " +
                $"got '{firstWord}'. Use pg_execute for writes (disabled by default).");
        }
    }

    public static void EnsureSingleStatement(string sql)
    {
        var body = StripStringsAndComments(sql);
        EnsureSingleStatementBody(body);
    }

    private static void EnsureSingleStatementBody(string body)
    {
        var semicolon = body.IndexOf(';');
        if (semicolon >= 0 && body[(semicolon + 1)..].Trim().Length > 0)
        {
            throw new ArgumentException(
                "One statement per call. Send stacked statements as separate pg_query/pg_execute calls.");
        }
    }

    private static string FirstWord(string body)
    {
        var trimmed = body.TrimStart();
        while (trimmed.StartsWith('('))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        var end = trimmed.IndexOfAny([' ', '\t', '\r', '\n', '(']);
        return end < 0 ? trimmed : trimmed[..end];
    }

    // Replaces string literals, quoted identifiers and comments with spaces
    // (preserving length) so ';' and keyword scans ignore them. Handles
    // '--' line comments, '/* */' blocks, '...' ('...' escapes), "..." and
    // $tag$ dollar-quoted bodies.
    private static string StripStringsAndComments(string sql)
    {
        var chars = sql.ToCharArray();
        var i = 0;
        while (i < chars.Length)
        {
            if (chars[i] == '-' && i + 1 < chars.Length && chars[i + 1] == '-')
            {
                BlankToEndOfLine(chars, ref i);
            }
            else if (chars[i] == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
            {
                BlankBlockComment(chars, ref i);
            }
            else if (chars[i] == '\'')
            {
                BlankQuoted(chars, ref i, '\'');
            }
            else if (chars[i] == '"')
            {
                BlankQuoted(chars, ref i, '"');
            }
            else if (chars[i] == '$')
            {
                if (!TryBlankDollarQuoted(chars, sql, ref i))
                {
                    i++;
                }
            }
            else
            {
                i++;
            }
        }

        return new string(chars);
    }

    private static void BlankToEndOfLine(char[] chars, ref int i)
    {
        chars[i] = chars[i + 1] = ' ';
        i += 2;
        while (i < chars.Length && chars[i] != '\n')
        {
            chars[i] = ' ';
            i++;
        }
    }

    private static void BlankBlockComment(char[] chars, ref int i)
    {
        chars[i] = chars[i + 1] = ' ';
        i += 2;
        while (i < chars.Length && !(chars[i - 1] == '*' && chars[i] == '/'))
        {
            chars[i] = ' ';
            i++;
        }

        if (i < chars.Length)
        {
            chars[i] = ' ';
            i++;
        }
    }

    private static void BlankQuoted(char[] chars, ref int i, char quote)
    {
        chars[i] = ' ';
        i++;
        while (i < chars.Length)
        {
            if (chars[i] == quote)
            {
                // '' is an escaped quote inside a single-quoted literal.
                if (quote == '\'' && i + 1 < chars.Length && chars[i + 1] == '\'')
                {
                    chars[i] = chars[i + 1] = ' ';
                    i += 2;
                    continue;
                }

                chars[i] = ' ';
                i++;
                return;
            }

            if (chars[i] != '\n')
            {
                chars[i] = ' ';
            }

            i++;
        }
    }

    private static bool TryBlankDollarQuoted(char[] chars, string sql, ref int i)
    {
        var matchEnd = sql.IndexOf('$', i + 1);
        if (matchEnd < 0)
        {
            return false;
        }

        var tag = sql[i..(matchEnd + 1)];
        if (!IsDollarTag(tag))
        {
            return false;
        }

        var closer = sql.IndexOf(tag, matchEnd + 1, StringComparison.Ordinal);
        var end = closer < 0 ? chars.Length : closer + tag.Length;
        while (i < end)
        {
            if (chars[i] != '\n')
            {
                chars[i] = ' ';
            }

            i++;
        }

        return true;
    }

    private static bool IsDollarTag(string tag)
    {
        for (var k = 1; k < tag.Length - 1; k++)
        {
            var c = tag[k];
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
