namespace Roadbed.Logging.Mcp.Lib;

using System;
using System.Collections.Generic;

/// <summary>
/// Maps between Microsoft.Extensions.Logging numeric levels and their names.
/// The <c>log_entries.log_level</c> column stores the integer; tools accept and
/// return the names, and a <c>min_level</c> filter becomes <c>log_level &gt;= n</c>.
/// </summary>
public static class LogLevels
{
    #region Private Fields

    private static readonly string[] NamesByValue =
    {
        "Trace",        // 0
        "Debug",        // 1
        "Information",  // 2
        "Warning",      // 3
        "Error",        // 4
        "Critical",     // 5
        "None",         // 6
    };

    private static readonly Dictionary<string, int> ValuesByName =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Trace"] = 0,
            ["Debug"] = 1,
            ["Information"] = 2,
            ["Warning"] = 3,
            ["Error"] = 4,
            ["Critical"] = 5,
            ["None"] = 6,
        };

    #endregion

    #region Public Methods

    /// <summary>
    /// Converts a numeric Microsoft.Extensions.Logging level to its name.
    /// </summary>
    /// <param name="level">The numeric level (0-6).</param>
    /// <returns>The level name, or <c>"Unknown"</c> if out of range.</returns>
    public static string ToName(int level)
    {
        if (level < 0 || level >= NamesByValue.Length)
        {
            return "Unknown";
        }

        return NamesByValue[level];
    }

    /// <summary>
    /// Attempts to resolve a level name (case-insensitive) to its numeric value.
    /// </summary>
    /// <param name="name">The level name, for example <c>"Warning"</c>.</param>
    /// <param name="value">The resolved numeric level when successful.</param>
    /// <returns><see langword="true"/> when the name is a known level.</returns>
    public static bool TryGetValue(string? name, out int value)
    {
        if (!string.IsNullOrWhiteSpace(name) && ValuesByName.TryGetValue(name.Trim(), out value))
        {
            return true;
        }

        value = -1;
        return false;
    }

    #endregion
}
