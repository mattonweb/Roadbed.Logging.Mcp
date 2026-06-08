namespace Roadbed.Logging.Mcp.Tools;

using System;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Guards the optional <c>run_readonly_query</c> tool. Accepts a single
/// <c>SELECT</c>/<c>WITH</c> statement, rejects statement chaining, comment
/// smuggling, and DDL/DML keywords, then wraps the query in a row-capping
/// <c>LIMIT</c>. The read-only DB account remains the authoritative backstop.
/// </summary>
internal static partial class AdHocSqlValidator
{
    #region Internal Methods

    public static (bool Ok, string Wrapped, string Error) Validate(string sql, int maxRows)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return (false, string.Empty, "Query text is required.");
        }

        var trimmed = sql.Trim().TrimEnd(';', ' ', '\t', '\r', '\n').Trim();
        if (trimmed.Length == 0)
        {
            return (false, string.Empty, "Query text is required.");
        }

        if (trimmed.Contains(';', StringComparison.Ordinal))
        {
            return (false, string.Empty, "Only a single statement is allowed (no ';' chaining).");
        }

        if (trimmed.Contains("--", StringComparison.Ordinal)
            || trimmed.Contains("/*", StringComparison.Ordinal)
            || trimmed.Contains('#', StringComparison.Ordinal))
        {
            return (false, string.Empty, "SQL comments are not allowed.");
        }

        if (!StartsWithSelect().IsMatch(trimmed))
        {
            return (false, string.Empty, "Only SELECT or WITH (CTE) queries are allowed.");
        }

        var forbidden = ForbiddenKeywords().Match(trimmed);
        if (forbidden.Success)
        {
            return (false, string.Empty, $"Disallowed keyword in query: {forbidden.Value.ToUpperInvariant()}.");
        }

        var wrapped = string.Create(
            CultureInfo.InvariantCulture,
            $"SELECT * FROM (\n{trimmed}\n) AS _adhoc LIMIT {maxRows + 1}");

        return (true, wrapped, string.Empty);
    }

    #endregion

    #region Private Methods

    [GeneratedRegex(
        @"^\s*\(*\s*(SELECT|WITH)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StartsWithSelect();

    [GeneratedRegex(
        @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|REPLACE|TRUNCATE|GRANT|REVOKE|CALL|EXEC|EXECUTE|LOAD|INTO|OUTFILE|DUMPFILE|HANDLER|LOCK|UNLOCK|SET|USE|RENAME|ATTACH|MERGE|DELIMITER)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenKeywords();

    #endregion
}
