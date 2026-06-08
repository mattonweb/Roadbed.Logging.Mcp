namespace Roadbed.Logging.Mcp.Data;

using System;

/*
 * Internal criteria objects carrying already-resolved query arguments (windows
 * defaulted, limits clamped) so repository methods avoid long parameter lists.
 */
internal sealed class ActivityListCriteria
{
    public DateTime Since { get; set; }

    public DateTime Until { get; set; }

    public string? Application { get; set; }

    public string? Environment { get; set; }

    public string? ActivityType { get; set; }

    public string? ActivityKey { get; set; }

    public string? Status { get; set; }

    public string? TargetContains { get; set; }

    public int Limit { get; set; }

    public string? Cursor { get; set; }
}

internal sealed class RecentFailuresCriteria
{
    public DateTime Since { get; set; }

    public DateTime Until { get; set; }

    public string? Application { get; set; }

    public string? ActivityKey { get; set; }

    public int Limit { get; set; }
}

internal sealed class ActivityLogsCriteria
{
    public string ActivityId { get; set; } = string.Empty;

    public int MinLevel { get; set; }

    public string? CategoryContains { get; set; }

    public string? MessageContains { get; set; }

    public bool ExceptionsOnly { get; set; }

    public DateTime Lo { get; set; }

    public DateTime Hi { get; set; }

    public int Limit { get; set; }

    public string? Cursor { get; set; }

    public bool Ascending { get; set; } = true;
}

internal sealed class LogSearchCriteria
{
    public DateTime Since { get; set; }

    public DateTime Until { get; set; }

    public int MinLevel { get; set; }

    public string? Application { get; set; }

    public string? CategoryContains { get; set; }

    public string? MessageContains { get; set; }

    public string? ExceptionType { get; set; }

    public int Limit { get; set; }

    public string? Cursor { get; set; }
}

internal sealed class HistoryCriteria
{
    public string? ActivityKey { get; set; }

    public string? ActivityType { get; set; }

    public string? Application { get; set; }

    public DateTime Since { get; set; }

    public DateTime Until { get; set; }

    public int Limit { get; set; }
}
