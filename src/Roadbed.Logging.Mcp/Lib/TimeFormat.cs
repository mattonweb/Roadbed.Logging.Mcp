namespace Roadbed.Logging.Mcp.Lib;

using System;
using System.Globalization;
using System.Text;

/// <summary>
/// UTC time and duration formatting helpers. Every stored timestamp is UTC; the
/// server emits ISO-8601 with a trailing <c>Z</c> and performs no timezone
/// conversion.
/// </summary>
public static class TimeFormat
{
    #region Public Methods

    /// <summary>
    /// Formats a database timestamp (assumed UTC, <c>DATETIME(6)</c>) as
    /// ISO-8601 with microsecond precision and a trailing <c>Z</c>.
    /// </summary>
    /// <param name="value">The timestamp, treated as UTC regardless of its <see cref="DateTimeKind"/>.</param>
    /// <returns>The ISO-8601 string, or <see langword="null"/> when <paramref name="value"/> is null.</returns>
    public static string? ToIso8601Utc(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        var utc = DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
        return utc.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Computes the elapsed milliseconds between a start and end timestamp.
    /// </summary>
    /// <param name="start">The start timestamp (UTC).</param>
    /// <param name="end">The end timestamp (UTC).</param>
    /// <returns>
    /// The whole-millisecond difference, or <see langword="null"/> when either
    /// value is null (for example, a run that has not completed).
    /// </returns>
    public static long? DurationMs(DateTime? start, DateTime? end)
    {
        if (start is null || end is null)
        {
            return null;
        }

        return (long)Math.Round((end.Value - start.Value).TotalMilliseconds);
    }

    /// <summary>
    /// Renders a millisecond duration as a compact human-readable string
    /// (for example <c>"1m 23s"</c> or <c>"450ms"</c>).
    /// </summary>
    /// <param name="milliseconds">The duration in milliseconds.</param>
    /// <returns>The formatted duration, or <see langword="null"/> when null.</returns>
    public static string? Humanize(long? milliseconds)
    {
        if (milliseconds is null)
        {
            return null;
        }

        var ms = milliseconds.Value;
        if (ms < 0)
        {
            return null;
        }

        if (ms < 1000)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{ms}ms");
        }

        var span = TimeSpan.FromMilliseconds(ms);
        var builder = new StringBuilder();

        if (span.Days > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{span.Days}d ");
        }

        if (span.Hours > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{span.Hours}h ");
        }

        if (span.Minutes > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{span.Minutes}m ");
        }

        builder.Append(CultureInfo.InvariantCulture, $"{span.Seconds}s");
        return builder.ToString();
    }

    #endregion
}
