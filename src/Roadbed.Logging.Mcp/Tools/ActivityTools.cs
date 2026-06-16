namespace Roadbed.Logging.Mcp.Tools;

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using global::Roadbed.Logging.Mcp.Configuration;
using global::Roadbed.Logging.Mcp.Data;
using global::Roadbed.Logging.Mcp.Lib;
using global::Roadbed.Logging.Mcp.Models;

/// <summary>
/// Activity list, drill, lineage, and history tools.
/// </summary>
[McpServerToolType]
public static class ActivityTools
{
    #region Public Methods

    /// <summary>
    /// Returns a filtered, keyset-paginated run list.
    /// </summary>
    /// <param name="repository">The injected repository.</param>
    /// <param name="registry">The injected source registry.</param>
    /// <param name="config">The injected configuration.</param>
    /// <param name="source">Optional source name.</param>
    /// <param name="since">Optional ISO-8601 window start.</param>
    /// <param name="until">Optional ISO-8601 window end.</param>
    /// <param name="application">Optional application filter.</param>
    /// <param name="environment">Optional environment filter.</param>
    /// <param name="activityType">Optional activity-type filter.</param>
    /// <param name="activityKey">Optional activity-key filter.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="targetContains">Optional target substring filter.</param>
    /// <param name="limit">Optional row limit.</param>
    /// <param name="cursor">Optional pagination cursor.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Compact JSON page of activity summaries.</returns>
    [McpServerTool(Name = "activities_list")]
    [Description("Filtered, newest-first run list with keyset pagination (next_cursor). Filters on created_on; "
        + "default window 7 days. Each row carries created_on for cheap by-id drilling.")]
    public static Task<string> ActivitiesList(
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
        [Description("Optional environment filter.")]
        string? environment = null,
        [Description("Optional activity-type filter.")]
        string? activityType = null,
        [Description("Optional activity-key filter.")]
        string? activityKey = null,
        [Description("Optional status filter: pending, running, succeeded, failed, canceled, skipped.")]
        string? status = null,
        [Description("Optional case-insensitive substring match on target.")]
        string? targetContains = null,
        [Description("Max rows (default 50, capped by server limits).")]
        int? limit = null,
        [Description("Opaque pagination cursor from a previous call's next_cursor.")]
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSupport.Guard(async () =>
        {
            var (factory, name) = ToolSupport.ResolveSource(registry, source);
            var (windowStart, windowEnd) = ToolSupport.ResolveWindow(since, until, config.DefaultWindowDays.Lists);
            var criteria = new ActivityListCriteria
            {
                Since = windowStart,
                Until = windowEnd,
                Application = application,
                Environment = environment,
                ActivityType = activityType,
                ActivityKey = activityKey,
                Status = status,
                TargetContains = targetContains,
                Limit = ToolSupport.ClampLimit(limit, defaultValue: 50, config.Limits.MaxActivities),
                Cursor = cursor,
            };
            var page = await repository.ActivitiesListAsync(
                factory, name, criteria, config.Limits.TextTruncateChars, cancellationToken);
            return Json.Serialize(page);
        });
    }

    /// <summary>
    /// Returns one full run record.
    /// </summary>
    /// <param name="repository">The injected repository.</param>
    /// <param name="registry">The injected source registry.</param>
    /// <param name="config">The injected configuration.</param>
    /// <param name="id">The activity ULID.</param>
    /// <param name="createdOn">Optional created_on prune hint.</param>
    /// <param name="full">Return untruncated error text when true.</param>
    /// <param name="source">Optional source name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Compact JSON activity detail, or a structured not-found error.</returns>
    [McpServerTool(Name = "activity_get")]
    [Description("One full run: parsed parameters/metrics, full or truncated error, log_level_counts, and "
        + "input/output lineage counts. Pass created_on (from a prior list) to prune to one partition; "
        + "otherwise the ULID timestamp is used to derive the prune window.")]
    public static Task<string> ActivityGet(
        LoggingRepository repository,
        ISourceRegistry registry,
        McpConfig config,
        [Description("The activity ULID (required).")]
        string id,
        [Description("Optional created_on prune hint, ISO-8601 UTC (from a preceding list call).")]
        string? createdOn = null,
        [Description("Return untruncated error text when true.")]
        bool full = false,
        [Description("Logical source name. Defaults to the configured default source.")]
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSupport.Guard(async () =>
        {
            var (factory, name) = ToolSupport.ResolveSource(registry, source);
            var hint = ToolSupport.ParseIsoOrNull(createdOn, "created_on");
            var truncate = full ? int.MaxValue : config.Limits.TextTruncateChars;
            var detail = await repository.ActivityGetAsync(factory, name, id, hint, truncate, cancellationToken);
            return detail is null
                ? ToolError.ToJson($"Activity '{id}' not found in source '{name}'.", "id")
                : Json.Serialize(detail);
        });
    }

    /// <summary>
    /// Resolves lineage edges (inputs/outputs) up to a depth.
    /// </summary>
    /// <param name="repository">The injected repository.</param>
    /// <param name="registry">The injected source registry.</param>
    /// <param name="id">The activity ULID.</param>
    /// <param name="direction">Edge direction.</param>
    /// <param name="depth">Traversal depth.</param>
    /// <param name="createdOn">Optional prune hint.</param>
    /// <param name="source">Optional source name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Compact JSON lineage result.</returns>
    [McpServerTool(Name = "activity_lineage")]
    [Description("Provenance edges resolved to summaries. direction = inputs | outputs | both (default both); "
        + "depth 1-3 (default 1). Related activities are resolved with ULID-derived partition pruning.")]
    public static Task<string> ActivityLineage(
        LoggingRepository repository,
        ISourceRegistry registry,
        [Description("The activity ULID (required).")]
        string id,
        [Description("Direction: inputs, outputs, or both (default).")]
        string? direction = null,
        [Description("Traversal depth 1-3 (default 1).")]
        int? depth = null,
        [Description("Optional created_on prune hint for the starting node, ISO-8601 UTC.")]
        string? createdOn = null,
        [Description("Logical source name. Defaults to the configured default source.")]
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSupport.Guard(async () =>
        {
            var (factory, name) = ToolSupport.ResolveSource(registry, source);
            var dir = string.IsNullOrWhiteSpace(direction) ? "both" : direction.Trim().ToLowerInvariant();
            if (dir != "inputs" && dir != "outputs" && dir != "both")
            {
                throw new ToolArgumentException("direction must be inputs, outputs, or both.", "direction");
            }

            var resolvedDepth = ToolSupport.ClampLimit(depth, defaultValue: 1, max: 3);
            var hint = ToolSupport.ParseIsoOrNull(createdOn, "created_on");
            var result = await repository.ActivityLineageAsync(
                factory, name, id, dir, resolvedDepth, hint, cancellationToken);
            return Json.Serialize(result);
        });
    }

    /// <summary>
    /// Returns one recurring workload's runs over time plus aggregate stats.
    /// </summary>
    /// <param name="repository">The injected repository.</param>
    /// <param name="registry">The injected source registry.</param>
    /// <param name="config">The injected configuration.</param>
    /// <param name="activityKey">The activity key (one of key/type required).</param>
    /// <param name="activityType">The activity type (one of key/type required).</param>
    /// <param name="application">Optional application filter.</param>
    /// <param name="since">Optional ISO-8601 window start.</param>
    /// <param name="until">Optional ISO-8601 window end.</param>
    /// <param name="limit">Optional row limit.</param>
    /// <param name="source">Optional source name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Compact JSON history result with stats (including true p95).</returns>
    [McpServerTool(Name = "activity_history")]
    [Description("One recurring workload over time for regression hunting. Provide activity_key OR activity_type. "
        + "Filters on created_on; default window 90 days, capped to the ~12-month activity retention. Returns "
        + "runs plus stats including min/avg/p95/max duration. stats.skipped counts skipped runs (terminal "
        + "no-op status); skipped is EXCLUDED from success_rate on both sides, so an all-skipped window "
        + "returns null instead of 0%.")]
    public static Task<string> ActivityHistory(
        LoggingRepository repository,
        ISourceRegistry registry,
        McpConfig config,
        [Description("Activity key (provide this or activity_type).")]
        string? activityKey = null,
        [Description("Activity type (provide this or activity_key).")]
        string? activityType = null,
        [Description("Optional application filter.")]
        string? application = null,
        [Description("Window start, ISO-8601 UTC (filters created_on). Default: 90 days ago.")]
        string? since = null,
        [Description("Window end, ISO-8601 UTC (filters created_on). Default: now.")]
        string? until = null,
        [Description("Max runs (default 30, capped by server limits).")]
        int? limit = null,
        [Description("Logical source name. Defaults to the configured default source.")]
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSupport.Guard(async () =>
        {
            if (string.IsNullOrWhiteSpace(activityKey) && string.IsNullOrWhiteSpace(activityType))
            {
                throw new ToolArgumentException(
                    "Provide activity_key or activity_type.", "activity_key");
            }

            var (factory, name) = ToolSupport.ResolveSource(registry, source);
            var (windowStart, windowEnd) = ToolSupport.ResolveWindow(since, until, config.DefaultWindowDays.History);
            var (cappedSince, note) = ToolSupport.CapHistoryWindow(windowStart, windowEnd);
            var criteria = new HistoryCriteria
            {
                ActivityKey = activityKey,
                ActivityType = activityType,
                Application = application,
                Since = cappedSince,
                Until = windowEnd,
                Limit = ToolSupport.ClampLimit(limit, defaultValue: 30, config.Limits.MaxActivities),
            };
            var result = await repository.ActivityHistoryAsync(
                factory, name, criteria, config.Limits.TextTruncateChars, note, cancellationToken);
            return Json.Serialize(result);
        });
    }

    #endregion
}
