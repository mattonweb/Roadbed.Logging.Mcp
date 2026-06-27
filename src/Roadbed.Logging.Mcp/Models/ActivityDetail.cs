namespace Roadbed.Logging.Mcp.Models;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// One full run record returned by <c>activity_get</c>. Extends
/// <see cref="ActivitySummary"/> with correlation, Quartz/host, parsed
/// JSON, and rollup-count fields.
/// </summary>
public sealed class ActivityDetail : ActivitySummary
{
    #region Public Properties

    /// <summary>Gets or sets the parent activity id (UUIDv7).</summary>
    [JsonPropertyName("parent_activity_id")]
    public string? ParentActivityId { get; set; }

    /// <summary>Gets or sets the root activity id (UUIDv7).</summary>
    [JsonPropertyName("root_activity_id")]
    public string? RootActivityId { get; set; }

    /// <summary>Gets or sets the W3C trace id.</summary>
    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }

    /// <summary>Gets or sets the W3C span id.</summary>
    [JsonPropertyName("span_id")]
    public string? SpanId { get; set; }

    /// <summary>Gets or sets the last heartbeat timestamp (UTC).</summary>
    [JsonPropertyName("last_heartbeat_on_utc")]
    public string? LastHeartbeatOnUtc { get; set; }

    /// <summary>Gets or sets the parsed <c>parameters</c> JSON object.</summary>
    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; set; }

    /// <summary>Gets or sets the parsed <c>metrics</c> JSON object.</summary>
    [JsonPropertyName("metrics")]
    public JsonElement? Metrics { get; set; }

    /// <summary>Gets or sets the account or principal that created the run.</summary>
    [JsonPropertyName("created_by")]
    public string? CreatedBy { get; set; }

    /// <summary>Gets or sets the Quartz scheduler instance id.</summary>
    [JsonPropertyName("scheduler_instance_id")]
    public string? SchedulerInstanceId { get; set; }

    /// <summary>Gets or sets the Quartz fire instance id.</summary>
    [JsonPropertyName("fire_instance_id")]
    public string? FireInstanceId { get; set; }

    /// <summary>Gets or sets the Quartz job name.</summary>
    [JsonPropertyName("quartz_job_name")]
    public string? QuartzJobName { get; set; }

    /// <summary>Gets or sets the Quartz job group.</summary>
    [JsonPropertyName("quartz_job_group")]
    public string? QuartzJobGroup { get; set; }

    /// <summary>Gets or sets the Quartz trigger name.</summary>
    [JsonPropertyName("quartz_trigger_name")]
    public string? QuartzTriggerName { get; set; }

    /// <summary>Gets or sets the Quartz trigger group.</summary>
    [JsonPropertyName("quartz_trigger_group")]
    public string? QuartzTriggerGroup { get; set; }

    /// <summary>Gets or sets the OS process id that ran the activity.</summary>
    [JsonPropertyName("process_id")]
    public int? ProcessId { get; set; }

    /// <summary>Gets or sets the per-level log counts for the run.</summary>
    [JsonPropertyName("log_level_counts")]
    public LevelCounts? LogLevelCounts { get; set; }

    /// <summary>Gets or sets the number of upstream lineage inputs.</summary>
    [JsonPropertyName("input_count")]
    public long? InputCount { get; set; }

    /// <summary>Gets or sets the number of downstream lineage outputs.</summary>
    [JsonPropertyName("output_count")]
    public long? OutputCount { get; set; }

    #endregion
}
