namespace Roadbed.Logging.Mcp.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Lean per-run summary returned by list and triage tools. Timestamps are
/// ISO-8601 UTC strings; <c>status</c> is a name; <c>duration</c> is precomputed.
/// </summary>
public class ActivitySummary
{
    #region Public Properties

    /// <summary>Gets or sets the activity ULID.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the partition-key creation timestamp (UTC, prune hint for by-id calls).</summary>
    [JsonPropertyName("created_on_utc")]
    public string CreatedOnUtc { get; set; } = string.Empty;

    /// <summary>Gets or sets the originating application.</summary>
    [JsonPropertyName("application")]
    public string? Application { get; set; }

    /// <summary>Gets or sets the environment (for example <c>Production</c>).</summary>
    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    /// <summary>Gets or sets the activity type.</summary>
    [JsonPropertyName("activity_type")]
    public string? ActivityType { get; set; }

    /// <summary>Gets or sets the recurring-workload key.</summary>
    [JsonPropertyName("activity_key")]
    public string? ActivityKey { get; set; }

    /// <summary>Gets or sets the work target (table, file, endpoint, ...).</summary>
    [JsonPropertyName("target")]
    public string? Target { get; set; }

    /// <summary>Gets or sets the run status name.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the start timestamp (UTC, display only).</summary>
    [JsonPropertyName("started_on_utc")]
    public string? StartedOnUtc { get; set; }

    /// <summary>Gets or sets the completion timestamp (UTC, display only).</summary>
    [JsonPropertyName("completed_on_utc")]
    public string? CompletedOnUtc { get; set; }

    /// <summary>Gets or sets the run duration in milliseconds (null if not completed).</summary>
    [JsonPropertyName("duration_ms")]
    public long? DurationMs { get; set; }

    /// <summary>Gets or sets the human-readable duration.</summary>
    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    /// <summary>Gets or sets the number of records impacted by the run.</summary>
    [JsonPropertyName("records_impacted")]
    public long? RecordsImpacted { get; set; }

    /// <summary>Gets or sets the truncated error text (populated by failure-oriented tools).</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Gets or sets a value indicating whether <see cref="Error"/> was truncated.</summary>
    [JsonPropertyName("error_truncated")]
    public bool? ErrorTruncated { get; set; }

    /// <summary>Gets or sets the error type (exception type name).</summary>
    [JsonPropertyName("error_type")]
    public string? ErrorType { get; set; }

    /// <summary>Gets or sets the host that ran the activity.</summary>
    [JsonPropertyName("host")]
    public string? Host { get; set; }

    /// <summary>Gets or sets the logical source the row came from.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    #endregion
}
