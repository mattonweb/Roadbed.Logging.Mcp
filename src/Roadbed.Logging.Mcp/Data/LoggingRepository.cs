namespace Roadbed.Logging.Mcp.Data;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using global::Roadbed;
using global::Roadbed.Data;
using global::Roadbed.Data.MySql;
using global::Roadbed.Logging.Mcp.Lib;
using global::Roadbed.Logging.Mcp.Models;

/// <summary>
/// Read-only data access over the Roadbed.Logging schema. Every query filters on
/// the partition key (<c>created_on</c> for activity, <c>event_time_utc</c> for
/// logs) so monthly partitions prune. The server only ever issues <c>SELECT</c>.
/// </summary>
public sealed class LoggingRepository : BaseClassWithLogging
{
    #region Private Fields

    /// <summary>Log retention horizon in days (operator drops older partitions).</summary>
    public const int LogRetentionDays = 90;

    /// <summary>Activity retention horizon in days (operator drops older partitions).</summary>
    public const int ActivityRetentionDays = 365;

    private const string ActivitySummaryColumns =
        "a.id, a.created_on, a.application, a.environment, a.activity_type, a.activity_key, " +
        "a.target, a.status, a.started_on, a.completed_on, a.records_impacted, a.error_type, a.host";

    private const string ActivityDetailColumns = ActivitySummaryColumns +
        ", a.last_heartbeat_on, a.parameters, a.metrics, a.error, a.created_by, a.process_id, " +
        "a.parent_activity_id, a.root_activity_id, a.trace_id, a.span_id, " +
        "a.scheduler_instance_id, a.fire_instance_id, a.quartz_job_name, a.quartz_job_group, " +
        "a.quartz_trigger_name, a.quartz_trigger_group";

    private const string LogColumns =
        "l.id, l.event_time_utc, l.log_level, l.category, l.event_id, l.event_name, l.message, " +
        "l.message_template, l.properties, l.exception, l.exception_type, l.activity_id, " +
        "l.trace_id, l.span_id, l.application, l.host";

    #endregion

    #region Public Constructors

    /// <summary>
    /// Initializes static members of the <see cref="LoggingRepository"/> class.
    /// </summary>
    static LoggingRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingRepository"/> class.
    /// </summary>
    /// <param name="logger">The logger (diagnostics go to stderr).</param>
    public LoggingRepository(ILogger<LoggingRepository> logger)
        : base(logger)
    {
    }

    #endregion

    #region Public Methods - Orientation & Triage

    /// <summary>
    /// Returns the per-application footprint within the window.
    /// </summary>
    /// <param name="factory">The resolved source connection factory.</param>
    /// <param name="source">The resolved source name.</param>
    /// <param name="since">Window start (UTC, on <c>created_on</c>).</param>
    /// <param name="until">Window end (UTC, on <c>created_on</c>).</param>
    /// <param name="topKeys">Max activity keys per application.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The catalog entries.</returns>
    internal async Task<IReadOnlyList<CatalogEntry>> CatalogAsync(
        IDataConnectionFactory factory,
        string source,
        DateTime since,
        DateTime until,
        int topKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var aggRequest = new DataExecutorRequest(
            "SELECT a.application, COUNT(*) AS run_count, MIN(a.created_on) AS first_seen, " +
            "MAX(a.created_on) AS last_seen, " +
            "GROUP_CONCAT(DISTINCT a.activity_type ORDER BY a.activity_type SEPARATOR ',') AS types " +
            "FROM activity AS a " +
            "WHERE a.created_on >= @since AND a.created_on < @until " +
            "GROUP BY a.application ORDER BY a.application")
        {
            RetriesEnabled = false,
            Parameters = new { since, until },
        };

        var keyRequest = new DataExecutorRequest(
            "SELECT a.application, a.activity_key, COUNT(*) AS cnt FROM activity AS a " +
            "WHERE a.created_on >= @since AND a.created_on < @until AND a.activity_key IS NOT NULL " +
            "GROUP BY a.application, a.activity_key")
        {
            RetriesEnabled = false,
            Parameters = new { since, until },
        };

        var aggs = await MySqlExecutor.QueryAsync<CatalogAggRow>(
            aggRequest, factory, this.Logger, cancellationToken);
        var keys = await MySqlExecutor.QueryAsync<CatalogKeyRow>(
            keyRequest, factory, this.Logger, cancellationToken);

        var keysByApp = keys
            .GroupBy(k => k.Application ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(k => k.Cnt)
                    .Take(topKeys)
                    .Select(k => k.ActivityKey ?? string.Empty)
                    .ToArray(),
                StringComparer.Ordinal);

        return aggs.Select(a => new CatalogEntry
        {
            Application = a.Application,
            ActivityTypes = string.IsNullOrEmpty(a.Types)
                ? Array.Empty<string>()
                : a.Types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ActivityKeys = keysByApp.TryGetValue(a.Application ?? string.Empty, out var k)
                ? k
                : Array.Empty<string>(),
            RunCount = a.RunCount,
            FirstSeenUtc = TimeFormat.ToIso8601Utc(a.FirstSeen),
            LastSeenUtc = TimeFormat.ToIso8601Utc(a.LastSeen),
            Source = source,
        }).ToList();
    }

    /// <summary>
    /// Returns a per-application (optionally per-type) health rollup.
    /// </summary>
    /// <param name="factory">The resolved source connection factory.</param>
    /// <param name="source">The resolved source name.</param>
    /// <param name="since">Window start (UTC, on <c>created_on</c>).</param>
    /// <param name="until">Window end (UTC, on <c>created_on</c>).</param>
    /// <param name="application">Optional application filter.</param>
    /// <param name="groupByType">Group by application and activity type when true.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rollup rows.</returns>
    internal async Task<IReadOnlyList<FleetOverviewRow>> FleetOverviewAsync(
        IDataConnectionFactory factory,
        string source,
        DateTime since,
        DateTime until,
        string? application,
        bool groupByType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var typeSelect = groupByType ? ", a.activity_type" : string.Empty;
        var groupBy = groupByType ? "a.application, a.activity_type" : "a.application";

        var sql = new StringBuilder()
            .Append("SELECT a.application").Append(typeSelect)
            .Append(", COUNT(*) AS runs")
            .Append(", CAST(SUM(a.status = 'succeeded') AS SIGNED) AS succeeded")
            .Append(", CAST(SUM(a.status = 'failed') AS SIGNED) AS failed")
            .Append(", CAST(SUM(a.status = 'canceled') AS SIGNED) AS canceled")
            .Append(", CAST(SUM(a.status = 'running') AS SIGNED) AS running")
            .Append(", AVG(CASE WHEN a.completed_on IS NOT NULL AND a.started_on IS NOT NULL ")
            .Append("THEN TIMESTAMPDIFF(MICROSECOND, a.started_on, a.completed_on) / 1000 END) AS avg_duration_ms")
            .Append(", CAST(SUM(a.records_impacted) AS SIGNED) AS total_records_impacted")
            .Append(", MAX(a.created_on) AS last_run")
            .Append(", MAX(CASE WHEN a.status IN ('failed', 'canceled') THEN a.created_on END) AS last_failure")
            .Append(" FROM activity AS a WHERE a.created_on >= @since AND a.created_on < @until");

        if (!string.IsNullOrWhiteSpace(application))
        {
            sql.Append(" AND a.application = @application");
        }

        sql.Append(" GROUP BY ").Append(groupBy).Append(" ORDER BY ").Append(groupBy);

        var request = new DataExecutorRequest(sql.ToString())
        {
            RetriesEnabled = false,
            Parameters = new { since, until, application },
        };

        var rows = await MySqlExecutor.QueryAsync<FleetRow>(request, factory, this.Logger, cancellationToken);

        return rows.Select(r => new FleetOverviewRow
        {
            Application = r.Application,
            ActivityType = groupByType ? r.ActivityType : null,
            Runs = r.Runs,
            Succeeded = r.Succeeded,
            Failed = r.Failed,
            Canceled = r.Canceled,
            Running = r.Running,
            SuccessRate = SuccessRate(r.Succeeded, r.Failed, r.Canceled),
            AvgDurationMs = r.AvgDurationMs is null ? null : (long)Math.Round(r.AvgDurationMs.Value),
            P95DurationMs = null, // Percentiles are surfaced by activity_history (single workload).
            TotalRecordsImpacted = r.TotalRecordsImpacted,
            LastRunUtc = TimeFormat.ToIso8601Utc(r.LastRun),
            LastFailureUtc = TimeFormat.ToIso8601Utc(r.LastFailure),
            Source = source,
        }).ToList();
    }

    /// <summary>
    /// Returns the newest failed/canceled runs across the fleet.
    /// </summary>
    /// <param name="factory">The resolved source connection factory.</param>
    /// <param name="source">The resolved source name.</param>
    /// <param name="criteria">The resolved criteria.</param>
    /// <param name="truncateChars">Truncation threshold for the error text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The failure summaries.</returns>
    internal async Task<IReadOnlyList<ActivitySummary>> RecentFailuresAsync(
        IDataConnectionFactory factory,
        string source,
        RecentFailuresCriteria criteria,
        int truncateChars,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(criteria);

        var sql = new StringBuilder()
            .Append("SELECT ").Append(ActivitySummaryColumns).Append(", a.error")
            .Append(" FROM activity AS a")
            .Append(" WHERE a.created_on >= @since AND a.created_on < @until")
            .Append(" AND a.status IN ('failed', 'canceled')");

        if (!string.IsNullOrWhiteSpace(criteria.Application))
        {
            sql.Append(" AND a.application = @application");
        }

        if (!string.IsNullOrWhiteSpace(criteria.ActivityKey))
        {
            sql.Append(" AND a.activity_key = @activityKey");
        }

        sql.Append(" ORDER BY a.created_on DESC, a.id DESC LIMIT @limit");

        var request = new DataExecutorRequest(sql.ToString())
        {
            RetriesEnabled = false,
            Parameters = new
            {
                since = criteria.Since,
                until = criteria.Until,
                application = criteria.Application,
                activityKey = criteria.ActivityKey,
                limit = criteria.Limit,
            },
        };

        var rows = await MySqlExecutor.QueryAsync<ActivityRow>(request, factory, this.Logger, cancellationToken);
        return rows.Select(r => MapSummary(r, source, truncateChars, includeError: true)).ToList();
    }

    #endregion

    #region Public Methods - List & Drill

    /// <summary>
    /// Returns a filtered, keyset-paginated run list.
    /// </summary>
    /// <param name="factory">The resolved source connection factory.</param>
    /// <param name="source">The resolved source name.</param>
    /// <param name="criteria">The resolved criteria.</param>
    /// <param name="truncateChars">Truncation threshold (unused for list; kept for symmetry).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A page of summaries plus the next cursor.</returns>
    internal async Task<PagedResult<ActivitySummary>> ActivitiesListAsync(
        IDataConnectionFactory factory,
        string source,
        ActivityListCriteria criteria,
        int truncateChars,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(criteria);

        var parameters = new DynamicParameters();
        parameters.Add("since", criteria.Since);
        parameters.Add("until", criteria.Until);

        var sql = new StringBuilder()
            .Append("SELECT ").Append(ActivitySummaryColumns)
            .Append(" FROM activity AS a")
            .Append(" WHERE a.created_on >= @since AND a.created_on < @until");

        AppendEquals(sql, parameters, "a.application", "application", criteria.Application);
        AppendEquals(sql, parameters, "a.environment", "environment", criteria.Environment);
        AppendEquals(sql, parameters, "a.activity_type", "activityType", criteria.ActivityType);
        AppendEquals(sql, parameters, "a.activity_key", "activityKey", criteria.ActivityKey);
        AppendEquals(sql, parameters, "a.status", "status", criteria.Status);
        AppendLike(sql, parameters, "a.target", "target", criteria.TargetContains);

        if (KeysetCursor.TryDecode(criteria.Cursor, out var ck, out var cid))
        {
            sql.Append(" AND (a.created_on < @ck OR (a.created_on = @ck AND a.id < @cid))");
            parameters.Add("ck", ck);
            parameters.Add("cid", cid);
        }

        sql.Append(" ORDER BY a.created_on DESC, a.id DESC LIMIT @limit");
        parameters.Add("limit", criteria.Limit + 1);

        var request = new DataExecutorRequest(sql.ToString()) { Parameters = parameters, RetriesEnabled = false };
        var rows = (await MySqlExecutor.QueryAsync<ActivityRow>(
            request, factory, this.Logger, cancellationToken)).ToList();

        var hasMore = rows.Count > criteria.Limit;
        var page = (hasMore ? rows.Take(criteria.Limit) : rows)
            .Select(r => MapSummary(r, source, truncateChars, includeError: false))
            .ToArray();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = rows[criteria.Limit - 1];
            nextCursor = KeysetCursor.Encode(last.CreatedOn, last.Id);
        }

        return new PagedResult<ActivitySummary> { Items = page, NextCursor = nextCursor, Source = source };
    }

    /// <summary>
    /// Returns one full run record, with parsed JSON, log-level counts, and
    /// lineage counts. Uses a <c>created_on</c> prune window derived from the
    /// optional hint or the ULID timestamp.
    /// </summary>
    /// <param name="factory">The resolved source connection factory.</param>
    /// <param name="source">The resolved source name.</param>
    /// <param name="id">The activity ULID.</param>
    /// <param name="createdOnHint">Optional <c>created_on</c> prune hint.</param>
    /// <param name="truncateChars">Truncation threshold for the error text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The detail, or <see langword="null"/> when not found.</returns>
    internal async Task<ActivityDetail?> ActivityGetAsync(
        IDataConnectionFactory factory,
        string source,
        string id,
        DateTime? createdOnHint,
        int truncateChars,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var center = createdOnHint ?? UlidTimestamp.TryGetTimestampUtc(id);
        var parameters = new DynamicParameters();
        parameters.Add("id", id);

        var sql = new StringBuilder()
            .Append("SELECT ").Append(ActivityDetailColumns)
            .Append(" FROM activity AS a WHERE a.id = @id");

        if (center is not null)
        {
            var (lo, hi) = Bracket(center.Value, days: 2);
            sql.Append(" AND a.created_on >= @lo AND a.created_on < @hi");
            parameters.Add("lo", lo);
            parameters.Add("hi", hi);
        }

        sql.Append(" LIMIT 1");

        var request = new DataExecutorRequest(sql.ToString()) { Parameters = parameters, RetriesEnabled = false };
        var row = await MySqlExecutor.QuerySingleOrDefaultAsync<ActivityRow>(
            request, factory, this.Logger, cancellationToken);

        if (row is null)
        {
            return null;
        }

        var counts = await LogLevelCountsAsync(factory, row, this.Logger, cancellationToken);
        var inputCount = await CountInputsAsync(factory, id, center, this.Logger, cancellationToken);
        var outputCount = await CountOutputsAsync(factory, id, this.Logger, cancellationToken);

        return MapDetail(row, source, truncateChars, counts, inputCount, outputCount);
    }

    #endregion

    #region Public Methods - Logs

    /// <summary>
    /// Returns a cheap pre-pull triage summary of a run's logs.
    /// </summary>
    /// <param name="factory">The resolved source connection factory.</param>
    /// <param name="source">The resolved source name.</param>
    /// <param name="activityId">The owning activity ULID.</param>
    /// <param name="createdOnHint">Optional timestamp hint for the log window.</param>
    /// <param name="topCategories">Max categories / exception types to return.</param>
    /// <param name="truncateChars">Truncation threshold for sample messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The log summary.</returns>
    internal async Task<LogSummary> ActivityLogSummaryAsync(
        IDataConnectionFactory factory,
        string source,
        string activityId,
        DateTime? createdOnHint,
        int topCategories,
        int truncateChars,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);

        var center = createdOnHint ?? UlidTimestamp.TryGetTimestampUtc(activityId);
        var (lo, hi) = center is not null ? Bracket(center.Value, days: 2) : (DateTime.MinValue, DateTime.MaxValue);
        var bound = center is not null;
        var window = bound ? " AND l.event_time_utc >= @lo AND l.event_time_utc < @hi" : string.Empty;
        var parameters = new { activityId, lo, hi };

        var statsRequest = new DataExecutorRequest(
            "SELECT COUNT(*) AS total, MIN(l.event_time_utc) AS first_time, MAX(l.event_time_utc) AS last_time " +
            "FROM log_entries AS l WHERE l.activity_id = @activityId" + window)
        {
            RetriesEnabled = false,
            Parameters = parameters,
        };

        var levelRequest = new DataExecutorRequest(
            "SELECT l.log_level, COUNT(*) AS cnt FROM log_entries AS l " +
            "WHERE l.activity_id = @activityId" + window + " GROUP BY l.log_level")
        {
            RetriesEnabled = false,
            Parameters = parameters,
        };

        var categoryRequest = new DataExecutorRequest(
            "SELECT l.category, COUNT(*) AS cnt FROM log_entries AS l " +
            "WHERE l.activity_id = @activityId" + window +
            " GROUP BY l.category ORDER BY cnt DESC LIMIT @top")
        {
            RetriesEnabled = false,
            Parameters = new { activityId, lo, hi, top = topCategories },
        };

        var exceptionRequest = new DataExecutorRequest(
            "SELECT l.exception_type, COUNT(*) AS cnt, MIN(l.message) AS sample_message FROM log_entries AS l " +
            "WHERE l.activity_id = @activityId" + window + " AND l.exception_type IS NOT NULL " +
            "GROUP BY l.exception_type ORDER BY cnt DESC LIMIT @top")
        {
            RetriesEnabled = false,
            Parameters = new { activityId, lo, hi, top = topCategories },
        };

        var stats = await MySqlExecutor.QuerySingleOrDefaultAsync<TimeStatRow>(
            statsRequest, factory, this.Logger, cancellationToken);
        var levels = await MySqlExecutor.QueryAsync<LevelCountRow>(
            levelRequest, factory, this.Logger, cancellationToken);
        var categories = await MySqlExecutor.QueryAsync<CategoryRow>(
            categoryRequest, factory, this.Logger, cancellationToken);
        var exceptions = await MySqlExecutor.QueryAsync<ExceptionRow>(
            exceptionRequest, factory, this.Logger, cancellationToken);

        return new LogSummary
        {
            Total = stats?.Total ?? 0,
            CountsByLevel = ToLevelCounts(levels),
            FirstEventUtc = TimeFormat.ToIso8601Utc(stats?.FirstTime),
            LastEventUtc = TimeFormat.ToIso8601Utc(stats?.LastTime),
            TopCategories = categories.Select(c => new CategoryCount { Category = c.Category, Count = c.Cnt }).ToArray(),
            ExceptionTypes = exceptions.Select(e => new ExceptionTypeSummary
            {
                Type = e.ExceptionType,
                Count = e.Cnt,
                SampleMessage = TextTruncation.Truncate(e.SampleMessage, truncateChars).Value,
            }).ToArray(),
            Note = RetentionNoteIfStale(center),
            Source = source,
        };
    }

    /// <summary>
    /// Returns a run's raw log lines (keyset-paginated).
    /// </summary>
    /// <param name="factory">The resolved source connection factory.</param>
    /// <param name="source">The resolved source name.</param>
    /// <param name="criteria">The resolved criteria.</param>
    /// <param name="truncateChars">Truncation threshold for message/exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A page of log entries plus the next cursor.</returns>
    internal async Task<PagedResult<LogEntry>> ActivityLogsAsync(
        IDataConnectionFactory factory,
        string source,
        ActivityLogsCriteria criteria,
        int truncateChars,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(criteria);

        var parameters = new DynamicParameters();
        parameters.Add("activityId", criteria.ActivityId);
        parameters.Add("lo", criteria.Lo);
        parameters.Add("hi", criteria.Hi);
        parameters.Add("minLevel", criteria.MinLevel);

        var sql = new StringBuilder()
            .Append("SELECT ").Append(LogColumns)
            .Append(" FROM log_entries AS l")
            .Append(" WHERE l.activity_id = @activityId")
            .Append(" AND l.event_time_utc >= @lo AND l.event_time_utc < @hi")
            .Append(" AND l.log_level >= @minLevel");

        AppendLike(sql, parameters, "l.category", "category", criteria.CategoryContains);
        AppendLike(sql, parameters, "l.message", "message", criteria.MessageContains);
        if (criteria.ExceptionsOnly)
        {
            sql.Append(" AND l.exception_type IS NOT NULL");
        }

        AppendLogKeyset(sql, parameters, criteria.Cursor, criteria.Ascending);
        AppendLogOrder(sql, criteria.Ascending);
        parameters.Add("limit", criteria.Limit + 1);

        var request = new DataExecutorRequest(sql.ToString()) { Parameters = parameters, RetriesEnabled = false };
        var rows = (await MySqlExecutor.QueryAsync<LogRow>(
            request, factory, this.Logger, cancellationToken)).ToList();

        return BuildLogPage(rows, source, criteria.Limit, truncateChars, RetentionNoteIfStale(criteria.Hi));
    }

    /// <summary>
    /// Searches log lines across activities within a window.
    /// </summary>
    /// <param name="factory">The resolved source connection factory.</param>
    /// <param name="source">The resolved source name.</param>
    /// <param name="criteria">The resolved criteria.</param>
    /// <param name="truncateChars">Truncation threshold for message/exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A page of log entries plus the next cursor.</returns>
    internal async Task<PagedResult<LogEntry>> LogsSearchAsync(
        IDataConnectionFactory factory,
        string source,
        LogSearchCriteria criteria,
        int truncateChars,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(criteria);

        var parameters = new DynamicParameters();
        parameters.Add("since", criteria.Since);
        parameters.Add("until", criteria.Until);
        parameters.Add("minLevel", criteria.MinLevel);

        var sql = new StringBuilder()
            .Append("SELECT ").Append(LogColumns)
            .Append(" FROM log_entries AS l")
            .Append(" WHERE l.event_time_utc >= @since AND l.event_time_utc < @until")
            .Append(" AND l.log_level >= @minLevel");

        AppendEquals(sql, parameters, "l.application", "application", criteria.Application);
        AppendEquals(sql, parameters, "l.exception_type", "exceptionType", criteria.ExceptionType);
        AppendLike(sql, parameters, "l.category", "category", criteria.CategoryContains);
        AppendLike(sql, parameters, "l.message", "message", criteria.MessageContains);

        // logs_search orders newest-first.
        if (KeysetCursor.TryDecode(criteria.Cursor, out var ck, out var cid) && long.TryParse(
                cid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cidLong))
        {
            sql.Append(" AND (l.event_time_utc < @ck OR (l.event_time_utc = @ck AND l.id < @cid))");
            parameters.Add("ck", ck);
            parameters.Add("cid", cidLong);
        }

        sql.Append(" ORDER BY l.event_time_utc DESC, l.id DESC LIMIT @limit");
        parameters.Add("limit", criteria.Limit + 1);

        var request = new DataExecutorRequest(sql.ToString()) { Parameters = parameters, RetriesEnabled = false };
        var rows = (await MySqlExecutor.QueryAsync<LogRow>(
            request, factory, this.Logger, cancellationToken)).ToList();

        return BuildLogPage(rows, source, criteria.Limit, truncateChars, note: null);
    }

    #endregion

    #region Public Methods - Lineage & History

    /// <summary>
    /// Resolves lineage edges (inputs/outputs) up to a depth, each as a summary.
    /// </summary>
    /// <param name="factory">The resolved source connection factory.</param>
    /// <param name="source">The resolved source name.</param>
    /// <param name="id">The activity ULID.</param>
    /// <param name="direction">One of <c>inputs</c>, <c>outputs</c>, or <c>both</c>.</param>
    /// <param name="depth">Traversal depth (1-3).</param>
    /// <param name="createdOnHint">Optional prune hint for the starting node.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved lineage.</returns>
    internal async Task<LineageResult> ActivityLineageAsync(
        IDataConnectionFactory factory,
        string source,
        string id,
        string direction,
        int depth,
        DateTime? createdOnHint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var result = new LineageResult { Source = source };
        var wantInputs = direction is "inputs" or "both";
        var wantOutputs = direction is "outputs" or "both";

        if (wantInputs)
        {
            result.Inputs = (await TraverseAsync(factory, source, id, depth, forward: true, this.Logger, cancellationToken))
                .ToArray();
        }

        if (wantOutputs)
        {
            result.Outputs = (await TraverseAsync(factory, source, id, depth, forward: false, this.Logger, cancellationToken))
                .ToArray();
        }

        return result;
    }

    /// <summary>
    /// Returns one recurring workload's runs over time plus aggregate stats.
    /// </summary>
    /// <param name="factory">The resolved source connection factory.</param>
    /// <param name="source">The resolved source name.</param>
    /// <param name="criteria">The resolved criteria.</param>
    /// <param name="truncateChars">Truncation threshold for error text.</param>
    /// <param name="note">An advisory note (for example, retention cap applied).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The history result.</returns>
    internal async Task<ActivityHistoryResult> ActivityHistoryAsync(
        IDataConnectionFactory factory,
        string source,
        HistoryCriteria criteria,
        int truncateChars,
        string? note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(criteria);

        var sql = new StringBuilder()
            .Append("SELECT ").Append(ActivitySummaryColumns).Append(", a.error")
            .Append(" FROM activity AS a")
            .Append(" WHERE a.created_on >= @since AND a.created_on < @until");

        if (!string.IsNullOrWhiteSpace(criteria.ActivityKey))
        {
            sql.Append(" AND a.activity_key = @activityKey");
        }

        if (!string.IsNullOrWhiteSpace(criteria.ActivityType))
        {
            sql.Append(" AND a.activity_type = @activityType");
        }

        if (!string.IsNullOrWhiteSpace(criteria.Application))
        {
            sql.Append(" AND a.application = @application");
        }

        sql.Append(" ORDER BY a.created_on DESC, a.id DESC LIMIT @limit");

        var request = new DataExecutorRequest(sql.ToString())
        {
            RetriesEnabled = false,
            Parameters = new
            {
                since = criteria.Since,
                until = criteria.Until,
                activityKey = criteria.ActivityKey,
                activityType = criteria.ActivityType,
                application = criteria.Application,
                limit = criteria.Limit,
            },
        };

        var rows = (await MySqlExecutor.QueryAsync<ActivityRow>(
            request, factory, this.Logger, cancellationToken)).ToList();

        var runs = rows.Select(r => MapSummary(r, source, truncateChars, includeError: true)).ToArray();
        return new ActivityHistoryResult
        {
            Runs = runs,
            Stats = ComputeStats(rows),
            Note = note,
            Source = source,
        };
    }

    #endregion

    #region Public Methods - Ad-hoc

    /// <summary>
    /// Executes a pre-validated, pre-wrapped read-only query and returns generic
    /// columns and rows. The caller is responsible for validating the SQL.
    /// </summary>
    /// <param name="factory">The resolved source connection factory.</param>
    /// <param name="source">The resolved source name.</param>
    /// <param name="wrappedSql">The validated, LIMIT-wrapped SQL.</param>
    /// <param name="maxRows">The row cap (the query fetches one extra to detect truncation).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generic result set.</returns>
    internal async Task<AdHocQueryResult> RunReadonlyQueryAsync(
        IDataConnectionFactory factory,
        string source,
        string wrappedSql,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(wrappedSql);

        using var connection = await factory.CreateOpenConnectionAsync(cancellationToken);
        var command = new CommandDefinition(wrappedSql, cancellationToken: cancellationToken);
        var raw = (await connection.QueryAsync(command)).Cast<IDictionary<string, object>>().ToList();

        var truncated = raw.Count > maxRows;
        var kept = truncated ? raw.Take(maxRows).ToList() : raw;
        var columns = raw.Count > 0 ? raw[0].Keys.ToArray() : Array.Empty<string>();

        var resultRows = kept
            .Select(r => columns.Select(c => r.TryGetValue(c, out var v) ? v : null).ToArray())
            .ToArray();

        return new AdHocQueryResult
        {
            Columns = columns,
            Rows = resultRows,
            RowCount = resultRows.Length,
            Truncated = truncated,
            Source = source,
        };
    }

    #endregion

    #region Private Methods - Lineage traversal

    private static async Task<List<LineageEdge>> TraverseAsync(
        IDataConnectionFactory factory,
        string source,
        string startId,
        int depth,
        bool forward,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var edges = new List<LineageEdge>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { startId };
        var frontier = new List<string> { startId };

        for (var level = 1; level <= depth && frontier.Count > 0; level++)
        {
            var column = forward ? "activity_id" : "input_activity_id";
            var request = new DataExecutorRequest(
                $"SELECT ai.activity_id, ai.input_activity_id, ai.input_role, ai.created_on " +
                $"FROM activity_input AS ai WHERE ai.{column} IN @ids")
            {
                RetriesEnabled = false,
                Parameters = new { ids = frontier },
            };

            var links = (await MySqlExecutor.QueryAsync<LineageLinkRow>(
                request, factory, logger, cancellationToken)).ToList();
            if (links.Count == 0)
            {
                break;
            }

            var relatedIds = links
                .Select(l => forward ? l.InputActivityId : l.ActivityId)
                .Where(rid => seen.Add(rid))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var summaries = await ResolveSummariesAsync(factory, source, relatedIds, logger, cancellationToken);

            foreach (var link in links)
            {
                var relatedId = forward ? link.InputActivityId : link.ActivityId;
                if (!summaries.TryGetValue(relatedId, out var summary))
                {
                    continue;
                }

                edges.Add(ToEdge(link.InputRole, relatedId, summary, level));
            }

            frontier = relatedIds;
        }

        return edges;
    }

    private static async Task<Dictionary<string, ActivitySummary>> ResolveSummariesAsync(
        IDataConnectionFactory factory,
        string source,
        IReadOnlyCollection<string> ids,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<string, ActivitySummary>(StringComparer.Ordinal);
        }

        var parameters = new DynamicParameters();
        parameters.Add("ids", ids);

        var sql = new StringBuilder()
            .Append("SELECT ").Append(ActivitySummaryColumns)
            .Append(" FROM activity AS a WHERE a.id IN @ids");

        // Bound created_on by the ULID-derived window across the batch to prune partitions.
        var stamps = ids.Select(UlidTimestamp.TryGetTimestampUtc).Where(t => t is not null).Select(t => t!.Value).ToList();
        if (stamps.Count == ids.Count)
        {
            var lo = Bracket(stamps.Min(), days: 2).Lo;
            var hi = Bracket(stamps.Max(), days: 2).Hi;
            sql.Append(" AND a.created_on >= @lo AND a.created_on < @hi");
            parameters.Add("lo", lo);
            parameters.Add("hi", hi);
        }

        var request = new DataExecutorRequest(sql.ToString()) { Parameters = parameters, RetriesEnabled = false };
        var rows = await MySqlExecutor.QueryAsync<ActivityRow>(request, factory, logger, cancellationToken);

        return rows
            .Select(r => MapSummary(r, source, int.MaxValue, includeError: false))
            .ToDictionary(s => s.Id, s => s, StringComparer.Ordinal);
    }

    #endregion

    #region Private Methods - Counts

    private static async Task<LevelCounts> LogLevelCountsAsync(
        IDataConnectionFactory factory,
        ActivityRow row,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var lo = (row.StartedOn ?? row.CreatedOn).AddDays(-1);
        var hi = (row.CompletedOn ?? row.CreatedOn).AddDays(1);
        var request = new DataExecutorRequest(
            "SELECT l.log_level, COUNT(*) AS cnt FROM log_entries AS l " +
            "WHERE l.activity_id = @id AND l.event_time_utc >= @lo AND l.event_time_utc < @hi " +
            "GROUP BY l.log_level")
        {
            RetriesEnabled = false,
            Parameters = new { id = row.Id, lo, hi },
        };

        var levels = await MySqlExecutor.QueryAsync<LevelCountRow>(
            request, factory, logger, cancellationToken);
        return ToLevelCounts(levels);
    }

    private static async Task<long> CountInputsAsync(
        IDataConnectionFactory factory,
        string id,
        DateTime? center,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("id", id);
        var sql = new StringBuilder("SELECT COUNT(*) FROM activity_input AS ai WHERE ai.activity_id = @id");
        if (center is not null)
        {
            var (lo, hi) = Bracket(center.Value, days: 2);
            sql.Append(" AND ai.created_on >= @lo AND ai.created_on < @hi");
            parameters.Add("lo", lo);
            parameters.Add("hi", hi);
        }

        var request = new DataExecutorRequest(sql.ToString()) { Parameters = parameters, RetriesEnabled = false };
        return await MySqlExecutor.ExecuteScalarAsync<long>(request, factory, logger, cancellationToken);
    }

    private static async Task<long> CountOutputsAsync(
        IDataConnectionFactory factory,
        string id,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Reverse edges have no created_on bound because downstream runs occur
        // later, and the reverse index on input_activity_id keeps the count cheap.
        var request = new DataExecutorRequest(
            "SELECT COUNT(*) FROM activity_input AS ai WHERE ai.input_activity_id = @id")
        {
            RetriesEnabled = false,
            Parameters = new { id },
        };
        return await MySqlExecutor.ExecuteScalarAsync<long>(request, factory, logger, cancellationToken);
    }

    #endregion

    #region Private Methods - Mapping

    private static ActivitySummary MapSummary(ActivityRow r, string source, int truncateChars, bool includeError)
    {
        var (error, errorTruncated) = includeError
            ? TextTruncation.Truncate(r.Error, truncateChars)
            : (null, false);
        var durationMs = TimeFormat.DurationMs(r.StartedOn, r.CompletedOn);

        return new ActivitySummary
        {
            Id = r.Id,
            CreatedOnUtc = TimeFormat.ToIso8601Utc(r.CreatedOn) ?? string.Empty,
            Application = r.Application,
            Environment = r.Environment,
            ActivityType = r.ActivityType,
            ActivityKey = r.ActivityKey,
            Target = r.Target,
            Status = r.Status ?? string.Empty,
            StartedOnUtc = TimeFormat.ToIso8601Utc(r.StartedOn),
            CompletedOnUtc = TimeFormat.ToIso8601Utc(r.CompletedOn),
            DurationMs = durationMs,
            Duration = TimeFormat.Humanize(durationMs),
            RecordsImpacted = r.RecordsImpacted,
            Error = includeError ? error : null,
            ErrorTruncated = includeError && error is not null ? errorTruncated : null,
            ErrorType = r.ErrorType,
            Host = r.Host,
            Source = source,
        };
    }

    private static ActivityDetail MapDetail(
        ActivityRow r,
        string source,
        int truncateChars,
        LevelCounts counts,
        long inputCount,
        long outputCount)
    {
        var (error, errorTruncated) = TextTruncation.Truncate(r.Error, truncateChars);
        var durationMs = TimeFormat.DurationMs(r.StartedOn, r.CompletedOn);

        return new ActivityDetail
        {
            Id = r.Id,
            CreatedOnUtc = TimeFormat.ToIso8601Utc(r.CreatedOn) ?? string.Empty,
            Application = r.Application,
            Environment = r.Environment,
            ActivityType = r.ActivityType,
            ActivityKey = r.ActivityKey,
            Target = r.Target,
            Status = r.Status ?? string.Empty,
            StartedOnUtc = TimeFormat.ToIso8601Utc(r.StartedOn),
            CompletedOnUtc = TimeFormat.ToIso8601Utc(r.CompletedOn),
            DurationMs = durationMs,
            Duration = TimeFormat.Humanize(durationMs),
            RecordsImpacted = r.RecordsImpacted,
            Error = error,
            ErrorTruncated = error is not null ? errorTruncated : null,
            ErrorType = r.ErrorType,
            Host = r.Host,
            Source = source,
            ParentActivityId = r.ParentActivityId,
            RootActivityId = r.RootActivityId,
            TraceId = r.TraceId,
            SpanId = r.SpanId,
            LastHeartbeatOnUtc = TimeFormat.ToIso8601Utc(r.LastHeartbeatOn),
            Parameters = Json.ParseColumn(r.Parameters),
            Metrics = Json.ParseColumn(r.Metrics),
            CreatedBy = r.CreatedBy?.ToString(CultureInfo.InvariantCulture),
            SchedulerInstanceId = r.SchedulerInstanceId,
            FireInstanceId = r.FireInstanceId,
            QuartzJobName = r.QuartzJobName,
            QuartzJobGroup = r.QuartzJobGroup,
            QuartzTriggerName = r.QuartzTriggerName,
            QuartzTriggerGroup = r.QuartzTriggerGroup,
            ProcessId = r.ProcessId,
            LogLevelCounts = counts,
            InputCount = inputCount,
            OutputCount = outputCount,
        };
    }

    private static LogEntry MapLog(LogRow r, string source, int truncateChars)
    {
        var (message, messageTruncated) = TextTruncation.Truncate(r.Message, truncateChars);
        var (exception, exceptionTruncated) = TextTruncation.Truncate(r.Exception, truncateChars);

        return new LogEntry
        {
            Id = r.Id,
            EventTimeUtc = TimeFormat.ToIso8601Utc(r.EventTimeUtc) ?? string.Empty,
            Level = LogLevels.ToName(r.LogLevel),
            Category = r.Category,
            EventId = r.EventId,
            EventName = r.EventName,
            Message = message,
            MessageTruncated = message is not null ? messageTruncated : null,
            MessageTemplate = r.MessageTemplate,
            Properties = Json.ParseColumn(r.Properties),
            ExceptionType = r.ExceptionType,
            Exception = exception,
            ExceptionTruncated = exception is not null ? exceptionTruncated : null,
            ActivityId = r.ActivityId,
            TraceId = r.TraceId,
            SpanId = r.SpanId,
            Application = r.Application,
            Host = r.Host,
            Source = source,
        };
    }

    private static LineageEdge ToEdge(string? role, string relatedId, ActivitySummary summary, int depth)
    {
        return new LineageEdge
        {
            Role = role,
            ActivityId = relatedId,
            ActivityType = summary.ActivityType,
            Target = summary.Target,
            Status = summary.Status,
            StartedOnUtc = summary.StartedOnUtc,
            CreatedOnUtc = summary.CreatedOnUtc,
            RecordsImpacted = summary.RecordsImpacted,
            Depth = depth,
        };
    }

    #endregion

    #region Private Methods - Helpers

    private static PagedResult<LogEntry> BuildLogPage(
        List<LogRow> rows,
        string source,
        int limit,
        int truncateChars,
        string? note)
    {
        var hasMore = rows.Count > limit;
        var page = (hasMore ? rows.Take(limit) : rows)
            .Select(r => MapLog(r, source, truncateChars))
            .ToArray();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = rows[limit - 1];
            nextCursor = KeysetCursor.Encode(
                last.EventTimeUtc, last.Id.ToString(CultureInfo.InvariantCulture));
        }

        return new PagedResult<LogEntry>
        {
            Items = page,
            NextCursor = nextCursor,
            Note = note,
            Source = source,
        };
    }

    private static (DateTime Lo, DateTime Hi) Bracket(DateTime center, int days)
    {
        return (center.AddDays(-days), center.AddDays(days));
    }

    private static double? SuccessRate(long succeeded, long failed, long canceled)
    {
        var completed = succeeded + failed + canceled;
        if (completed <= 0)
        {
            return null;
        }

        return Math.Round((double)succeeded / completed, 4);
    }

    private static LevelCounts ToLevelCounts(IEnumerable<LevelCountRow> rows)
    {
        var counts = new LevelCounts();
        foreach (var r in rows)
        {
            switch (r.LogLevel)
            {
                case 0: counts.Trace = r.Cnt; break;
                case 1: counts.Debug = r.Cnt; break;
                case 2: counts.Information = r.Cnt; break;
                case 3: counts.Warning = r.Cnt; break;
                case 4: counts.Error = r.Cnt; break;
                case 5: counts.Critical = r.Cnt; break;
                default: break;
            }
        }

        return counts;
    }

    private static HistoryStats ComputeStats(IReadOnlyCollection<ActivityRow> rows)
    {
        var durations = rows
            .Select(r => TimeFormat.DurationMs(r.StartedOn, r.CompletedOn))
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .OrderBy(d => d)
            .ToList();

        var succeeded = rows.Count(r => string.Equals(r.Status, "succeeded", StringComparison.Ordinal));
        var failed = rows.Count(r => string.Equals(r.Status, "failed", StringComparison.Ordinal));
        var canceled = rows.Count(r => string.Equals(r.Status, "canceled", StringComparison.Ordinal));
        var impacted = rows.Where(r => r.RecordsImpacted is not null).Select(r => r.RecordsImpacted!.Value).ToList();

        return new HistoryStats
        {
            Count = rows.Count,
            SuccessRate = SuccessRate(succeeded, failed, canceled),
            MinDurationMs = durations.Count > 0 ? durations[0] : null,
            AvgDurationMs = durations.Count > 0 ? (long)Math.Round(durations.Average()) : null,
            P95DurationMs = Percentile(durations, 0.95),
            MaxDurationMs = durations.Count > 0 ? durations[^1] : null,
            AvgRecordsImpacted = impacted.Count > 0 ? (long)Math.Round(impacted.Average()) : null,
        };
    }

    private static long? Percentile(IReadOnlyList<long> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return null;
        }

        if (sorted.Count == 1)
        {
            return sorted[0];
        }

        var rank = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi)
        {
            return sorted[lo];
        }

        var fraction = rank - lo;
        return (long)Math.Round(sorted[lo] + ((sorted[hi] - sorted[lo]) * fraction));
    }

    private static string? RetentionNoteIfStale(DateTime? windowEnd)
    {
        if (windowEnd is null)
        {
            return null;
        }

        var horizon = DateTime.UtcNow.AddDays(-LogRetentionDays);
        return windowEnd.Value < horizon
            ? "window predates the ~90-day log retention horizon; logs may have been dropped even if the activity resolves"
            : null;
    }

    private static void AppendEquals(
        StringBuilder sql,
        DynamicParameters parameters,
        string column,
        string paramName,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        sql.Append(" AND ").Append(column).Append(" = @").Append(paramName);
        parameters.Add(paramName, value);
    }

    private static void AppendLike(
        StringBuilder sql,
        DynamicParameters parameters,
        string column,
        string paramName,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // Bind the term as a parameter and assemble the wildcard pattern in SQL via CONCAT, so
        // the value is never formatted into the statement text (no injection). '%'/'_' inside the
        // term are escaped to match literally, using '!' as the LIKE escape char to avoid
        // backslash (whose meaning in string literals depends on sql_mode = NO_BACKSLASH_ESCAPES).
        sql.Append(" AND ").Append(column)
            .Append(" LIKE CONCAT('%', @").Append(paramName).Append(", '%') ESCAPE '!'");
        parameters.Add(paramName, EscapeForLike(value));
    }

    private static void AppendLogKeyset(
        StringBuilder sql,
        DynamicParameters parameters,
        string? cursor,
        bool ascending)
    {
        if (!KeysetCursor.TryDecode(cursor, out var ck, out var cid) || !long.TryParse(
                cid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cidLong))
        {
            return;
        }

        var comparison = ascending ? ">" : "<";
        sql.Append(" AND (l.event_time_utc ").Append(comparison).Append(" @ck OR (l.event_time_utc = @ck AND l.id ")
            .Append(comparison).Append(" @cid))");
        parameters.Add("ck", ck);
        parameters.Add("cid", cidLong);
    }

    private static void AppendLogOrder(StringBuilder sql, bool ascending)
    {
        sql.Append(ascending
            ? " ORDER BY l.event_time_utc ASC, l.id ASC LIMIT @limit"
            : " ORDER BY l.event_time_utc DESC, l.id DESC LIMIT @limit");
    }

    private static string EscapeForLike(string value)
    {
        // Escape the '!' escape char first, then the LIKE wildcards.
        return value
            .Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal);
    }

    #endregion
}
