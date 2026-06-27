namespace Roadbed.Logging.Mcp.Lib;

using System;
using System.Globalization;

/// <summary>
/// Decodes the embedded timestamp from a UUIDv7 activity identifier. UUIDv7
/// (RFC 9562) carries a 48-bit big-endian Unix-millisecond timestamp in its
/// most-significant bits — the first 12 hex digits of the canonical 8-4-4-4-12
/// string. That timestamp closely tracks <c>created_on</c>, so a by-id lookup
/// can derive a partition-prune window even when the caller supplies no
/// <c>created_on</c> hint.
/// </summary>
public static class Uuid7Timestamp
{
    #region Private Fields

    private const int TimestampHexLength = 12;

    private const int VersionNibbleIndex = 12;

    #endregion

    #region Public Methods

    /// <summary>
    /// Attempts to decode the millisecond Unix timestamp embedded in a UUIDv7.
    /// </summary>
    /// <param name="id">The activity id (a canonical 8-4-4-4-12 UUID string).</param>
    /// <returns>
    /// The decoded UTC timestamp, or <see langword="null"/> when the value is not
    /// a well-formed version-7 UUID.
    /// </returns>
    public static DateTime? TryGetTimestampUtc(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var guid))
        {
            return null;
        }

        // Normalize to 32 lowercase hex digits (no hyphens), independent of the
        // input casing/format. The canonical text order places the 48-bit
        // timestamp in the first 12 digits and the version nibble right after.
        var hex = guid.ToString("N");

        // Only a version-7 UUID embeds a Unix-ms timestamp; a v4 (random) value
        // or any other version has no meaningful time and must not drive pruning.
        if (hex[VersionNibbleIndex] != '7')
        {
            return null;
        }

        if (!long.TryParse(
                hex.AsSpan(0, TimestampHexLength),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var unixMs))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    #endregion
}
