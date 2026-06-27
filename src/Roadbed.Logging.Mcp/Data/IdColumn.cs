namespace Roadbed.Logging.Mcp.Data;

using System;

/*
 * Tolerant materialization for the uuid-typed id columns: activity.id, log_entries.activity_id,
 * both activity_input id columns, and activity's parent/root activity-id columns. These hold
 * UUIDv7 values as canonical 36-char text, which MySqlConnector surfaces as either a System.Guid
 * or a raw string depending on the connection's guid handling. The Dapper row POCOs declare these
 * columns as object (Dapper boxes whatever the provider returns) and the mapping layer funnels
 * them through here. A Guid is rendered as the canonical lowercase 8-4-4-4-12 string; a string is
 * preserved verbatim (already-canonical uuid text is untouched); a null, DBNull, or empty string
 * becomes null. This keeps the reader independent of the connector's guid handling instead of
 * throwing the parse error Dapper raises when a Guid lands in a string member.
 */
internal static class IdColumn
{
    public static string? ToCanonicalString(object? value)
    {
        return value switch
        {
            null or DBNull => null,
            Guid guid => guid.ToString(),
            string text => text.Length == 0 ? null : text,
            _ => value.ToString(),
        };
    }

    public static string ToCanonicalStringOrEmpty(object? value)
    {
        return ToCanonicalString(value) ?? string.Empty;
    }
}
