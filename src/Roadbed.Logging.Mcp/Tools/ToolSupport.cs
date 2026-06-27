namespace Roadbed.Logging.Mcp.Tools;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using global::Roadbed.Data;
using global::Roadbed.Logging.Mcp.Data;
using global::Roadbed.Logging.Mcp.Lib;
using global::Roadbed.Logging.Mcp.Models;

/// <summary>
/// Shared argument resolution for tools: source lookup, time-window defaulting,
/// limit clamping, level parsing, and log-window derivation. Invalid arguments
/// throw <see cref="ToolArgumentException"/>, which the tool boundary converts to
/// a structured error.
/// </summary>
internal static class ToolSupport
{
    #region Internal Methods

    public static async Task<string> Guard(Func<Task<string>> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            return await body();
        }
        catch (ToolArgumentException ex)
        {
            return ToolError.ToJson(ex.Message, ex.Argument);
        }
        catch (Exception ex)
        {
            // Messages from the data layer do not contain credentials.
            return ToolError.ToJson("Query failed: " + ex.Message);
        }
    }

    public static (IDataConnectionFactory Factory, string Name) ResolveSource(
        ISourceRegistry registry,
        string? source)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (registry.TryResolve(source, out var factory, out var name))
        {
            return (factory, name);
        }

        var known = string.Join(", ", registry.SourceNames);
        throw new ToolArgumentException($"Unknown source '{source}'. Known sources: {known}.", "source");
    }

    public static DateTime? ParseIsoOrNull(string? value, string argument)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed.UtcDateTime;
        }

        throw new ToolArgumentException($"'{value}' is not a valid ISO-8601 timestamp.", argument);
    }

    public static (DateTime Since, DateTime Until) ResolveWindow(string? since, string? until, int defaultDays)
    {
        var until2 = ParseIsoOrNull(until, "until") ?? DateTime.UtcNow;
        var since2 = ParseIsoOrNull(since, "since") ?? until2.AddDays(-defaultDays);

        if (since2 > until2)
        {
            throw new ToolArgumentException("'since' must not be after 'until'.", "since");
        }

        return (since2, until2);
    }

    public static (DateTime Since, string? Note) CapHistoryWindow(DateTime since, DateTime until)
    {
        var earliest = until.AddDays(-LoggingRepository.ActivityRetentionDays);
        if (since < earliest)
        {
            return (earliest, "window capped to the ~12-month activity retention horizon");
        }

        return (since, null);
    }

    public static int ClampLimit(int? requested, int defaultValue, int max)
    {
        var value = requested ?? defaultValue;
        if (value < 1)
        {
            value = 1;
        }

        return Math.Min(value, max);
    }

    public static int ResolveMinLevel(string? name, string defaultName)
    {
        var effective = string.IsNullOrWhiteSpace(name) ? defaultName : name;
        if (LogLevels.TryGetValue(effective, out var value))
        {
            return value;
        }

        throw new ToolArgumentException(
            $"Unknown log level '{name}'. Use Trace, Debug, Information, Warning, Error, Critical, or None.",
            "min_level");
    }

    public static (DateTime Lo, DateTime Hi) ComputeActivityLogWindow(
        string activityId,
        string? createdOnHint,
        string? startedOnHint,
        string? completedOnHint)
    {
        var created = ParseIsoOrNull(createdOnHint, "created_on");
        var started = ParseIsoOrNull(startedOnHint, "started_on");
        var completed = ParseIsoOrNull(completedOnHint, "completed_on");
        var idStamp = Uuid7Timestamp.TryGetTimestampUtc(activityId);

        var lower = new[] { started, created, idStamp }.FirstOrDefault(t => t is not null);
        var upper = new[] { completed, created, idStamp }.FirstOrDefault(t => t is not null);

        if (lower is null && upper is null)
        {
            // No hint and an undecodable id: fall back to the full log retention horizon.
            return (DateTime.UtcNow.AddDays(-LoggingRepository.LogRetentionDays), DateTime.UtcNow.AddDays(1));
        }

        var lowerTs = lower ?? upper;
        var upperTs = upper ?? lower;
        var lo = lowerTs!.Value.AddDays(-1);
        var hi = upperTs!.Value.AddDays(1);
        return (lo, hi);
    }

    #endregion
}
