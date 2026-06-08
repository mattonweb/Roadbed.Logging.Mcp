namespace Roadbed.Logging.Mcp.Lib;

/// <summary>
/// Truncates long free-text fields (<c>error</c>, <c>message</c>,
/// <c>exception</c>) so list and detail payloads stay token-efficient. Callers
/// surface a <c>&lt;field&gt;_truncated</c> marker and offer a <c>full=true</c>
/// escape to fetch the untruncated value.
/// </summary>
public static class TextTruncation
{
    #region Public Methods

    /// <summary>
    /// Truncates <paramref name="value"/> to at most <paramref name="maxChars"/>
    /// characters.
    /// </summary>
    /// <param name="value">The text to truncate; may be null.</param>
    /// <param name="maxChars">The maximum number of characters to keep.</param>
    /// <returns>
    /// A tuple of the (possibly shortened) text and a flag indicating whether
    /// truncation occurred.
    /// </returns>
    public static (string? Value, bool Truncated) Truncate(string? value, int maxChars)
    {
        if (value is null || maxChars <= 0 || value.Length <= maxChars)
        {
            return (value, false);
        }

        return (value[..maxChars], true);
    }

    #endregion
}
