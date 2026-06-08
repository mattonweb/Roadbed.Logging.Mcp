namespace Roadbed.Logging.Mcp.Lib;

using System;

/// <summary>
/// Decodes the embedded timestamp from a ULID. Activity identifiers are 26-char
/// Crockford base32 ULIDs whose first 10 characters encode a 48-bit millisecond
/// Unix timestamp. That timestamp closely tracks <c>created_on</c>, so a by-id
/// lookup can derive a partition-prune window even when the caller supplies no
/// <c>created_on</c> hint.
/// </summary>
public static class UlidTimestamp
{
    #region Private Fields

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int TimestampLength = 10;

    #endregion

    #region Public Methods

    /// <summary>
    /// Attempts to decode the millisecond Unix timestamp embedded in a ULID.
    /// </summary>
    /// <param name="ulid">The 26-character ULID string.</param>
    /// <returns>
    /// The decoded UTC timestamp, or <see langword="null"/> when the value is not
    /// a well-formed ULID.
    /// </returns>
    public static DateTime? TryGetTimestampUtc(string? ulid)
    {
        if (string.IsNullOrWhiteSpace(ulid) || ulid.Length != 26)
        {
            return null;
        }

        long value = 0;
        for (var i = 0; i < TimestampLength; i++)
        {
            var index = IndexOf(ulid[i]);
            if (index < 0)
            {
                return null;
            }

            value = (value << 5) | (uint)index;
        }

        if (value < 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    #endregion

    #region Private Methods

    private static int IndexOf(char raw)
    {
        var c = char.ToUpperInvariant(raw);

        // Crockford leniency for visually ambiguous characters.
        c = c switch
        {
            'I' or 'L' => '1',
            'O' => '0',
            _ => c,
        };

        return Alphabet.IndexOf(c, StringComparison.Ordinal);
    }

    #endregion
}
