namespace Roadbed.Logging.Mcp.Models;

using System.Text.Json.Serialization;

/// <summary>
/// One health-rollup row from <c>fleet_overview</c>, grouped by application (and
/// optionally activity type).
/// </summary>
public sealed class FleetOverviewRow
{
    #region Public Properties

    /// <summary>Gets or sets the application.</summary>
    [JsonPropertyName("application")]
    public string? Application { get; set; }

    /// <summary>Gets or sets the activity type (only when grouped by type).</summary>
    [JsonPropertyName("activity_type")]
    public string? ActivityType { get; set; }

    /// <summary>Gets or sets the total run count in the window.</summary>
    [JsonPropertyName("runs")]
    public long Runs { get; set; }

    /// <summary>Gets or sets the succeeded count.</summary>
    [JsonPropertyName("succeeded")]
    public long Succeeded { get; set; }

    /// <summary>Gets or sets the failed count.</summary>
    [JsonPropertyName("failed")]
    public long Failed { get; set; }

    /// <summary>Gets or sets the canceled count.</summary>
    [JsonPropertyName("canceled")]
    public long Canceled { get; set; }

    /// <summary>Gets or sets the currently-running count.</summary>
    [JsonPropertyName("running")]
    public long Running { get; set; }

    /// <summary>Gets or sets the skipped count (terminal status meaning the run ran to completion
    /// but intentionally did no work — e.g. a precondition was not met). Excluded from
    /// <see cref="SuccessRate"/> on both sides.</summary>
    [JsonPropertyName("skipped")]
    public long Skipped { get; set; }

    /// <summary>Gets or sets the success rate as a fraction of <c>succeeded / (succeeded + failed + canceled)</c>.
    /// Skipped runs are excluded from numerator and denominator, so a window of all-skipped runs returns
    /// <see langword="null"/> rather than 0%.</summary>
    [JsonPropertyName("success_rate")]
    public double? SuccessRate { get; set; }

    /// <summary>Gets or sets the average duration in milliseconds.</summary>
    [JsonPropertyName("avg_duration_ms")]
    public long? AvgDurationMs { get; set; }

    /// <summary>Gets or sets the 95th-percentile duration in milliseconds.</summary>
    [JsonPropertyName("p95_duration_ms")]
    public long? P95DurationMs { get; set; }

    /// <summary>Gets or sets the total records impacted across runs.</summary>
    [JsonPropertyName("total_records_impacted")]
    public long? TotalRecordsImpacted { get; set; }

    /// <summary>Gets or sets the most recent run timestamp (UTC).</summary>
    [JsonPropertyName("last_run_utc")]
    public string? LastRunUtc { get; set; }

    /// <summary>Gets or sets the most recent failure timestamp (UTC).</summary>
    [JsonPropertyName("last_failure_utc")]
    public string? LastFailureUtc { get; set; }

    /// <summary>Gets or sets the logical source.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    #endregion
}
