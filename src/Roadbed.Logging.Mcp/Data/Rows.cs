namespace Roadbed.Logging.Mcp.Data;

using System;

/*
 * Internal Dapper materialization rows. Column names map to these PascalCase
 * properties via Dapper's MatchNamesWithUnderscores (set in LoggingRepository).
 * XML docs are intentionally omitted (internal; see stylecop.json).
 */
internal sealed class ActivityRow
{
    public string Id { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? Application { get; set; }

    public string? Environment { get; set; }

    public string? ActivityType { get; set; }

    public string? ActivityKey { get; set; }

    public string? Target { get; set; }

    public string? Status { get; set; }

    public DateTime? StartedOn { get; set; }

    public DateTime? CompletedOn { get; set; }

    public DateTime? LastHeartbeatOn { get; set; }

    public long? RecordsImpacted { get; set; }

    public string? Error { get; set; }

    public string? ErrorType { get; set; }

    public string? Host { get; set; }

    public long? CreatedBy { get; set; }

    public int? ProcessId { get; set; }

    public string? ParentActivityId { get; set; }

    public string? RootActivityId { get; set; }

    public string? TraceId { get; set; }

    public string? SpanId { get; set; }

    public string? Parameters { get; set; }

    public string? Metrics { get; set; }

    public string? SchedulerInstanceId { get; set; }

    public string? FireInstanceId { get; set; }

    public string? QuartzJobName { get; set; }

    public string? QuartzJobGroup { get; set; }

    public string? QuartzTriggerName { get; set; }

    public string? QuartzTriggerGroup { get; set; }
}

internal sealed class LogRow
{
    public long Id { get; set; }

    public DateTime EventTimeUtc { get; set; }

    public int LogLevel { get; set; }

    public string? Category { get; set; }

    public int? EventId { get; set; }

    public string? EventName { get; set; }

    public string? Message { get; set; }

    public string? MessageTemplate { get; set; }

    public string? Properties { get; set; }

    public string? Exception { get; set; }

    public string? ExceptionType { get; set; }

    public string? ActivityId { get; set; }

    public string? TraceId { get; set; }

    public string? SpanId { get; set; }

    public string? Application { get; set; }

    public string? Host { get; set; }
}

internal sealed class FleetRow
{
    public string? Application { get; set; }

    public string? ActivityType { get; set; }

    public long Runs { get; set; }

    public long Succeeded { get; set; }

    public long Failed { get; set; }

    public long Canceled { get; set; }

    public long Running { get; set; }

    public double? AvgDurationMs { get; set; }

    public long? TotalRecordsImpacted { get; set; }

    public DateTime? LastRun { get; set; }

    public DateTime? LastFailure { get; set; }
}

internal sealed class CatalogAggRow
{
    public string? Application { get; set; }

    public long RunCount { get; set; }

    public DateTime? FirstSeen { get; set; }

    public DateTime? LastSeen { get; set; }

    public string? Types { get; set; }
}

internal sealed class CatalogKeyRow
{
    public string? Application { get; set; }

    public string? ActivityKey { get; set; }

    public long Cnt { get; set; }
}

internal sealed class LevelCountRow
{
    public int LogLevel { get; set; }

    public long Cnt { get; set; }
}

internal sealed class TimeStatRow
{
    public long Total { get; set; }

    public DateTime? FirstTime { get; set; }

    public DateTime? LastTime { get; set; }
}

internal sealed class CategoryRow
{
    public string? Category { get; set; }

    public long Cnt { get; set; }
}

internal sealed class ExceptionRow
{
    public string? ExceptionType { get; set; }

    public long Cnt { get; set; }

    public string? SampleMessage { get; set; }
}

internal sealed class LineageLinkRow
{
    public string ActivityId { get; set; } = string.Empty;

    public string InputActivityId { get; set; } = string.Empty;

    public string? InputRole { get; set; }

    public DateTime CreatedOn { get; set; }
}
