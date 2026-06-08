namespace Roadbed.Logging.Mcp.Tools;

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using global::Roadbed.Logging.Mcp.Configuration;
using global::Roadbed.Logging.Mcp.Data;
using global::Roadbed.Logging.Mcp.Lib;

/// <summary>
/// Daily-driver fleet-triage tools.
/// </summary>
[McpServerToolType]
public static class TriageTools
{
    #region Public Methods

    /// <summary>
    /// Returns a per-application (optionally per-type) health rollup.
    /// </summary>
    /// <param name="repository">The injected repository.</param>
    /// <param name="registry">The injected source registry.</param>
    /// <param name="config">The injected configuration.</param>
    /// <param name="source">Optional source name.</param>
    /// <param name="since">Optional ISO-8601 window start.</param>
    /// <param name="until">Optional ISO-8601 window end.</param>
    /// <param name="application">Optional application filter.</param>
    /// <param name="groupBy">Grouping mode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Compact JSON rollup rows.</returns>
    [McpServerTool(Name = "fleet_overview")]
    [Description("Per-application health rollup (runs, succeeded/failed/canceled/running, success_rate, "
        + "avg_duration_ms, total_records_impacted, last_run/last_failure). Filters on created_on; default "
        + "window 7 days. p95 is null here - use activity_history for percentiles on a single workload.")]
    public static Task<string> FleetOverview(
        LoggingRepository repository,
        ISourceRegistry registry,
        McpConfig config,
        [Description("Logical source name. Defaults to the configured default source.")]
        string? source = null,
        [Description("Window start, ISO-8601 UTC (filters created_on). Default: 7 days ago.")]
        string? since = null,
        [Description("Window end, ISO-8601 UTC (filters created_on). Default: now.")]
        string? until = null,
        [Description("Optional application filter.")]
        string? application = null,
        [Description("Grouping: 'application' (default) or 'application_type' (by application and activity type).")]
        string? groupBy = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSupport.Guard(async () =>
        {
            var (factory, name) = ToolSupport.ResolveSource(registry, source);
            var (windowStart, windowEnd) = ToolSupport.ResolveWindow(since, until, config.DefaultWindowDays.Overview);
            var byType = string.Equals(groupBy, "application_type", StringComparison.OrdinalIgnoreCase);
            var rows = await repository.FleetOverviewAsync(
                factory, name, windowStart, windowEnd, application, byType, cancellationToken);
            return Json.Serialize(rows);
        });
    }

    /// <summary>
    /// Returns the newest failed/canceled runs across the fleet.
    /// </summary>
    /// <param name="repository">The injected repository.</param>
    /// <param name="registry">The injected source registry.</param>
    /// <param name="config">The injected configuration.</param>
    /// <param name="source">Optional source name.</param>
    /// <param name="since">Optional ISO-8601 window start.</param>
    /// <param name="until">Optional ISO-8601 window end.</param>
    /// <param name="application">Optional application filter.</param>
    /// <param name="activityKey">Optional activity-key filter.</param>
    /// <param name="limit">Optional row limit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Compact JSON activity summaries with truncated error text.</returns>
    [McpServerTool(Name = "activities_recent_failures")]
    [Description("Newest failed/canceled runs across the fleet, with truncated error/error_type. Filters on "
        + "created_on; default window 7 days. Default limit 25.")]
    public static Task<string> ActivitiesRecentFailures(
        LoggingRepository repository,
        ISourceRegistry registry,
        McpConfig config,
        [Description("Logical source name. Defaults to the configured default source.")]
        string? source = null,
        [Description("Window start, ISO-8601 UTC (filters created_on). Default: 7 days ago.")]
        string? since = null,
        [Description("Window end, ISO-8601 UTC (filters created_on). Default: now.")]
        string? until = null,
        [Description("Optional application filter.")]
        string? application = null,
        [Description("Optional activity-key filter.")]
        string? activityKey = null,
        [Description("Max rows (default 25, capped by server limits).")]
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSupport.Guard(async () =>
        {
            var (factory, name) = ToolSupport.ResolveSource(registry, source);
            var (windowStart, windowEnd) = ToolSupport.ResolveWindow(since, until, config.DefaultWindowDays.Lists);
            var criteria = new RecentFailuresCriteria
            {
                Since = windowStart,
                Until = windowEnd,
                Application = application,
                ActivityKey = activityKey,
                Limit = ToolSupport.ClampLimit(limit, defaultValue: 25, config.Limits.MaxActivities),
            };
            var rows = await repository.RecentFailuresAsync(
                factory, name, criteria, config.Limits.TextTruncateChars, cancellationToken);
            return Json.Serialize(rows);
        });
    }

    #endregion
}
