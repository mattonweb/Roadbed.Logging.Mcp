namespace Roadbed.Logging.Mcp.Models;

using System;
using System.Text.Json.Serialization;

/// <summary>
/// One recurring workload's runs over time plus aggregate stats
/// (<c>activity_history</c>), for regression hunting. Bounded to the ~12-month
/// activity retention.
/// </summary>
public sealed class ActivityHistoryResult
{
    #region Public Properties

    /// <summary>Gets or sets the matching runs (most recent first).</summary>
    [JsonPropertyName("runs")]
    public ActivitySummary[] Runs { get; set; } = Array.Empty<ActivitySummary>();

    /// <summary>Gets or sets the aggregate statistics over the matched runs.</summary>
    [JsonPropertyName("stats")]
    public HistoryStats Stats { get; set; } = new HistoryStats();

    /// <summary>Gets or sets an advisory note (for example, retention cap applied).</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>Gets or sets the logical source.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    #endregion
}

/// <summary>
/// Aggregate duration and success statistics for a set of runs.
/// </summary>
public sealed class HistoryStats
{
    #region Public Properties

    /// <summary>Gets or sets the run count.</summary>
    [JsonPropertyName("count")]
    public long Count { get; set; }

    /// <summary>Gets or sets the success rate as a fraction of completed runs (0-1).</summary>
    [JsonPropertyName("success_rate")]
    public double? SuccessRate { get; set; }

    /// <summary>Gets or sets the minimum duration in milliseconds.</summary>
    [JsonPropertyName("min_duration_ms")]
    public long? MinDurationMs { get; set; }

    /// <summary>Gets or sets the average duration in milliseconds.</summary>
    [JsonPropertyName("avg_duration_ms")]
    public long? AvgDurationMs { get; set; }

    /// <summary>Gets or sets the 95th-percentile duration in milliseconds.</summary>
    [JsonPropertyName("p95_duration_ms")]
    public long? P95DurationMs { get; set; }

    /// <summary>Gets or sets the maximum duration in milliseconds.</summary>
    [JsonPropertyName("max_duration_ms")]
    public long? MaxDurationMs { get; set; }

    /// <summary>Gets or sets the average records impacted per run.</summary>
    [JsonPropertyName("avg_records_impacted")]
    public long? AvgRecordsImpacted { get; set; }

    #endregion
}
