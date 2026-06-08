namespace Roadbed.Logging.Mcp.Models;

using System;
using System.Text.Json.Serialization;

/// <summary>
/// Resolved lineage for one activity (<c>activity_lineage</c>): upstream inputs
/// and downstream outputs, each as a lean edge summary.
/// </summary>
public sealed class LineageResult
{
    #region Public Properties

    /// <summary>Gets or sets the upstream activities this run consumed.</summary>
    [JsonPropertyName("inputs")]
    public LineageEdge[] Inputs { get; set; } = Array.Empty<LineageEdge>();

    /// <summary>Gets or sets the downstream activities that consumed this run.</summary>
    [JsonPropertyName("outputs")]
    public LineageEdge[] Outputs { get; set; } = Array.Empty<LineageEdge>();

    /// <summary>Gets or sets the logical source.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    #endregion
}

/// <summary>
/// One lineage edge resolved to a summary of the related activity.
/// </summary>
public sealed class LineageEdge
{
    #region Public Properties

    /// <summary>Gets or sets the lineage role (for example <c>"source"</c>).</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>Gets or sets the related activity ULID.</summary>
    [JsonPropertyName("activity_id")]
    public string ActivityId { get; set; } = string.Empty;

    /// <summary>Gets or sets the related activity type.</summary>
    [JsonPropertyName("activity_type")]
    public string? ActivityType { get; set; }

    /// <summary>Gets or sets the related target.</summary>
    [JsonPropertyName("target")]
    public string? Target { get; set; }

    /// <summary>Gets or sets the related status name.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Gets or sets the related start timestamp (UTC).</summary>
    [JsonPropertyName("started_on_utc")]
    public string? StartedOnUtc { get; set; }

    /// <summary>Gets or sets the related creation timestamp (UTC, prune hint to drill).</summary>
    [JsonPropertyName("created_on_utc")]
    public string? CreatedOnUtc { get; set; }

    /// <summary>Gets or sets the related records-impacted count.</summary>
    [JsonPropertyName("records_impacted")]
    public long? RecordsImpacted { get; set; }

    /// <summary>Gets or sets the traversal depth at which this edge was found.</summary>
    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    #endregion
}
