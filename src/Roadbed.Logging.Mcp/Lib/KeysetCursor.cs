namespace Roadbed.Logging.Mcp.Lib;

using System;
using System.Globalization;
using System.Text;

/// <summary>
/// Opaque keyset-pagination cursor. Encodes the last row's
/// <c>(partition_key, id)</c> pair so the next page can resume with a
/// partition-prunable <c>WHERE (partition_key, id) &lt; (@key, @id)</c> predicate
/// instead of an <c>OFFSET</c> scan. The token is treated as opaque by callers.
/// </summary>
public static class KeysetCursor
{
    #region Private Fields

    private const char Separator = '|';

    #endregion

    #region Public Methods

    /// <summary>
    /// Encodes a partition key and identifier into an opaque cursor token.
    /// </summary>
    /// <param name="partitionKey">The partition-key timestamp of the last row.</param>
    /// <param name="id">The identifier of the last row (ULID or numeric, as text).</param>
    /// <returns>An opaque, round-trippable cursor token.</returns>
    public static string Encode(DateTime partitionKey, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{partitionKey.Ticks}{Separator}{id}");

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    /// <summary>
    /// Attempts to decode an opaque cursor token back into its parts.
    /// </summary>
    /// <param name="cursor">The cursor token previously produced by <see cref="Encode"/>.</param>
    /// <param name="partitionKey">The decoded partition-key timestamp (UTC).</param>
    /// <param name="id">The decoded identifier.</param>
    /// <returns><see langword="true"/> when the token is well-formed.</returns>
    public static bool TryDecode(string? cursor, out DateTime partitionKey, out string id)
    {
        partitionKey = default;
        id = string.Empty;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        }
        catch (FormatException)
        {
            return false;
        }

        var split = payload.IndexOf(Separator, StringComparison.Ordinal);
        if (split <= 0 || split >= payload.Length - 1)
        {
            return false;
        }

        if (!long.TryParse(
                payload.AsSpan(0, split),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var ticks))
        {
            return false;
        }

        try
        {
            partitionKey = new DateTime(ticks, DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var idStart = split + 1;
        id = payload[idStart..];
        return true;
    }

    #endregion
}
