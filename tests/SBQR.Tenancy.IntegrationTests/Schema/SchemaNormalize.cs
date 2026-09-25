using System.Text.RegularExpressions;

namespace SBQR.Tenancy.IntegrationTests.Schema;

/// <summary>
/// Shared normalization helpers used by both <see cref="PostgresSchemaReader"/>
/// and <see cref="EfModelSchemaReader"/> so the two read the same surface —
/// otherwise a <c>timestamp with time zone</c> on one side would compare unequal
/// to <c>timestamptz</c> on the other and the drift test would fire
/// pointlessly.
/// </summary>
internal static class SchemaNormalize
{
    /// <summary>PostgreSQL system columns that EF never models.</summary>
    public static bool IsSystemColumn(string name) =>
        name is "xmin" or "xmax" or "cmin" or "cmax" or "ctid" or "tableoid";

    /// <summary>
    /// Normalize the PostgreSQL <c>information_schema.data_type</c> spelling
    /// (e.g. <c>character varying</c>) into the EF-side column type
    /// (e.g. <c>varchar</c>). The length specifier (e.g. <c>(100)</c>) is
    /// dropped from both sides because PG's <c>information_schema</c> emits
    /// only the type name (no length), and EF's <c>HasColumnType</c>
    /// includes the length. The drift test compares lengths in a separate
    /// column-length check.
    /// </summary>
    public static string DataType(string raw)
    {
        var trimmed = raw.Trim().ToLowerInvariant();
        var baseType = trimmed switch
        {
            "character varying" => "varchar",
            "character"         => "char",
            "timestamp without time zone" => "timestamp",
            "timestamp with time zone"    => "timestamptz",
            "time without time zone"      => "time",
            "time with time zone"         => "timetz",
            _ => trimmed,
        };

        // Drop "(N)" / "(N,M)" length specifier.
        var parenStart = baseType.IndexOf('(');
        return parenStart > 0 ? baseType[..parenStart].Trim() : baseType;
    }

    /// <summary>
    /// Normalize a <c>DEFAULT</c> expression. PG's <c>information_schema</c>
    /// ships defaults wrapped in parens with the unqualified function name in
    /// quotes; EF's <c>HasDefaultValueSql</c> takes the raw SQL. Strip parens
    /// and lowercase so <c>now()</c> matches <c>now()</c>.
    /// </summary>
    public static string? DefaultExpression(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var trimmed = raw.Trim().TrimEnd(';');
        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Normalize a CHECK constraint expression. Both PG (<c>CHECK (...)</c>)
    /// and EF (<c>HasCheckConstraint(name, "x > 0")</c>) emit whitespace, case,
    /// and bracketing differently. Lowercase, collapse whitespace, strip the
    /// <c>CHECK</c> keyword and outer parens so they compare cleanly.
    /// </summary>
    public static string CheckExpression(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("CHECK ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["CHECK ".Length..].Trim();
        }

        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return Regex.Replace(trimmed, @"\s+", " ").ToLowerInvariant();
    }

    /// <summary>Lowercase the filter predicate (PG emits lowercase identifiers too).</summary>
    public static string? IndexFilter(string? predicate)
    {
        if (predicate is null)
        {
            return null;
        }

        var trimmed = Regex.Replace(predicate.Trim(), @"\s+", " ");

        // PG wraps partial-index predicates in `(...)` (it always has — it's a
        // bare predicate expression). EF's HasFilter("...") stores the bare
        // text without parens. Strip a single outer pair so they compare equal.
        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        // PG also rewrites predicates with implicit casts when the column type
        // differs from a literal (e.g. `status = 'ACTIVE'` becomes
        // `(status)::text = 'ACTIVE'::text` for varchar columns). Strip the
        // redundant `::type` and `(... )::type` casts so EF's bare predicate
        // matches the round-tripped PG output.
        trimmed = Regex.Replace(trimmed, @"\)\s*::\s*[a-zA-Z_]+", ")");
        trimmed = Regex.Replace(trimmed, @"::\s*[a-zA-Z_]+", string.Empty);

        // After the cast strip, a leftover `(status)` (column wrapped in
        // parens) may remain. Drop a no-op `(identifier)` pair.
        trimmed = Regex.Replace(trimmed, @"\(\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\)", "$1");

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.ToLowerInvariant();
    }
}
