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
/// Log summary, per-run log retrieval, and cross-run log search tools.
/// </summary>
[McpServerToolType]
public static class LogTools
{
    #region Public Methods

    /// <summary>
    /// Returns a cheap pre-pull triage summary of a run's logs.
    /// </summary>
    /// <param name="repository">The injected repository.</param>
    /// <param name="registry">The injected source registry.</param>
    /// <param name="config">The injected configuration.</param>
    /// <param name="activityId">The owning activity ULID.</param>
    /// <param name="createdOn">Optional timestamp hint.</param>
    /// <param name="source">Optional source name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Compact JSON log summary.</returns>
    [McpServerTool(Name = "activity_log_summary")]
    [Description("Cheap triage of a run's logs before pulling lines: total, counts_by_level, first/last event, "
        + "top categories, and exception types with a sample message. The log window is derived from the hint or "
        + "the ULID timestamp. Adds a note when the window predates the ~90-day log retention.")]
    public static Task<string> ActivityLogSummary(
        LoggingRepository repository,
        ISourceRegistry registry,
        McpConfig config,
        [Description("The owning activity ULID (required).")]
        string activityId,
        [Description("Optional timestamp hint, ISO-8601 UTC (created_on/started_on), to bound the log window.")]
        string? createdOn = null,
        [Description("Logical source name. Defaults to the configured default source.")]
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSupport.Guard(async () =>
        {
            var (factory, name) = ToolSupport.ResolveSource(registry, source);
            var hint = ToolSupport.ParseIsoOrNull(createdOn, "created_on");
            var summary = await repository.ActivityLogSummaryAsync(
                factory, name, activityId, hint, topCategories: 10, config.Limits.TextTruncateChars, cancellationToken);
            return Json.Serialize(summary);
        });
    }

    /// <summary>
    /// Returns a run's raw log lines (keyset-paginated).
    /// </summary>
    /// <param name="repository">The injected repository.</param>
    /// <param name="registry">The injected source registry.</param>
    /// <param name="config">The injected configuration.</param>
    /// <param name="activityId">The owning activity ULID.</param>
    /// <param name="minLevel">Minimum level name.</param>
    /// <param name="categoryContains">Optional category substring.</param>
    /// <param name="messageContains">Optional message substring.</param>
    /// <param name="exceptionsOnly">Restrict to rows with an exception.</param>
    /// <param name="createdOn">Optional created_on hint.</param>
    /// <param name="startedOn">Optional started_on hint.</param>
    /// <param name="completedOn">Optional completed_on hint.</param>
    /// <param name="limit">Optional row limit.</param>
    /// <param name="cursor">Optional pagination cursor.</param>
    /// <param name="order">Sort order.</param>
    /// <param name="full">Return untruncated message/exception when true.</param>
    /// <param name="source">Optional source name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Compact JSON page of log entries.</returns>
    [McpServerTool(Name = "activity_logs")]
    [Description("A run's raw log lines (keyset-paginated). min_level defaults to Information. Window hints "
        + "(created_on/started_on/completed_on) bound event_time_utc for partition pruning; the ULID timestamp is "
        + "used otherwise. Adds a note when the window predates the ~90-day log retention.")]
    public static Task<string> ActivityLogs(
        LoggingRepository repository,
        ISourceRegistry registry,
        McpConfig config,
        [Description("The owning activity ULID (required).")]
        string activityId,
        [Description("Minimum level: Trace, Debug, Information (default), Warning, Error, Critical.")]
        string? minLevel = null,
        [Description("Optional case-insensitive substring match on category.")]
        string? categoryContains = null,
        [Description("Optional case-insensitive substring match on message.")]
        string? messageContains = null,
        [Description("When true, only rows that carry an exception.")]
        bool exceptionsOnly = false,
        [Description("Optional created_on hint, ISO-8601 UTC.")]
        string? createdOn = null,
        [Description("Optional started_on hint, ISO-8601 UTC.")]
        string? startedOn = null,
        [Description("Optional completed_on hint, ISO-8601 UTC.")]
        string? completedOn = null,
        [Description("Max rows (default 200, capped by server limits).")]
        int? limit = null,
        [Description("Opaque pagination cursor from a previous call's next_cursor.")]
        string? cursor = null,
        [Description("Sort order: asc (default) or desc.")]
        string? order = null,
        [Description("Return untruncated message/exception text when true.")]
        bool full = false,
        [Description("Logical source name. Defaults to the configured default source.")]
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSupport.Guard(async () =>
        {
            var (factory, name) = ToolSupport.ResolveSource(registry, source);
            var (lo, hi) = ToolSupport.ComputeActivityLogWindow(activityId, createdOn, startedOn, completedOn);
            var criteria = new ActivityLogsCriteria
            {
                ActivityId = activityId,
                MinLevel = ToolSupport.ResolveMinLevel(minLevel, "Information"),
                CategoryContains = categoryContains,
                MessageContains = messageContains,
                ExceptionsOnly = exceptionsOnly,
                Lo = lo,
                Hi = hi,
                Limit = ToolSupport.ClampLimit(limit, defaultValue: 200, config.Limits.MaxLogRows),
                Cursor = cursor,
                Ascending = !string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase),
            };
            var truncate = full ? int.MaxValue : config.Limits.TextTruncateChars;
            var page = await repository.ActivityLogsAsync(factory, name, criteria, truncate, cancellationToken);
            return Json.Serialize(page);
        });
    }

    /// <summary>
    /// Searches log lines across activities within a window.
    /// </summary>
    /// <param name="repository">The injected repository.</param>
    /// <param name="registry">The injected source registry.</param>
    /// <param name="config">The injected configuration.</param>
    /// <param name="source">Optional source name.</param>
    /// <param name="since">Optional ISO-8601 window start.</param>
    /// <param name="until">Optional ISO-8601 window end.</param>
    /// <param name="minLevel">Minimum level name.</param>
    /// <param name="application">Optional application filter.</param>
    /// <param name="categoryContains">Optional category substring.</param>
    /// <param name="messageContains">Optional message substring.</param>
    /// <param name="exceptionType">Optional exact exception type.</param>
    /// <param name="limit">Optional row limit.</param>
    /// <param name="cursor">Optional pagination cursor.</param>
    /// <param name="full">Return untruncated message/exception when true.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Compact JSON page of log entries (each carrying activity_id to pivot).</returns>
    [McpServerTool(Name = "logs_search")]
    [Description("Cross-activity log search (newest-first, keyset-paginated). Filters on event_time_utc; default "
        + "window 24 hours, min_level Warning. Each row carries activity_id to pivot to activity_get. Logs are "
        + "retained ~90 days.")]
    public static Task<string> LogsSearch(
        LoggingRepository repository,
        ISourceRegistry registry,
        McpConfig config,
        [Description("Logical source name. Defaults to the configured default source.")]
        string? source = null,
        [Description("Window start, ISO-8601 UTC (filters event_time_utc). Default: 24 hours ago.")]
        string? since = null,
        [Description("Window end, ISO-8601 UTC (filters event_time_utc). Default: now.")]
        string? until = null,
        [Description("Minimum level: Trace, Debug, Information, Warning (default), Error, Critical.")]
        string? minLevel = null,
        [Description("Optional application filter.")]
        string? application = null,
        [Description("Optional case-insensitive substring match on category.")]
        string? categoryContains = null,
        [Description("Optional case-insensitive substring match on message.")]
        string? messageContains = null,
        [Description("Optional exact exception type filter.")]
        string? exceptionType = null,
        [Description("Max rows (default 100, capped by server limits).")]
        int? limit = null,
        [Description("Opaque pagination cursor from a previous call's next_cursor.")]
        string? cursor = null,
        [Description("Return untruncated message/exception text when true.")]
        bool full = false,
        CancellationToken cancellationToken = default)
    {
        return ToolSupport.Guard(async () =>
        {
            var (factory, name) = ToolSupport.ResolveSource(registry, source);
            var (windowStart, windowEnd) = ToolSupport.ResolveWindow(since, until, config.DefaultWindowDays.Logs);
            var criteria = new LogSearchCriteria
            {
                Since = windowStart,
                Until = windowEnd,
                MinLevel = ToolSupport.ResolveMinLevel(minLevel, "Warning"),
                Application = application,
                CategoryContains = categoryContains,
                MessageContains = messageContains,
                ExceptionType = exceptionType,
                Limit = ToolSupport.ClampLimit(limit, defaultValue: 100, config.Limits.MaxLogRows),
                Cursor = cursor,
            };
            var truncate = full ? int.MaxValue : config.Limits.TextTruncateChars;
            var page = await repository.LogsSearchAsync(factory, name, criteria, truncate, cancellationToken);
            return Json.Serialize(page);
        });
    }

    #endregion
}
