namespace Roadbed.Logging.Mcp.Models;

using System;
using System.Text.Json.Serialization;

/// <summary>
/// Cheap triage of a run's logs (<c>activity_log_summary</c>) before pulling
/// individual lines.
/// </summary>
public sealed class LogSummary
{
    #region Public Properties

    /// <summary>Gets or sets the total log row count.</summary>
    [JsonPropertyName("total")]
    public long Total { get; set; }

    /// <summary>Gets or sets the per-level counts.</summary>
    [JsonPropertyName("counts_by_level")]
    public LevelCounts CountsByLevel { get; set; } = new LevelCounts();

    /// <summary>Gets or sets the earliest event timestamp (UTC).</summary>
    [JsonPropertyName("first_event_utc")]
    public string? FirstEventUtc { get; set; }

    /// <summary>Gets or sets the latest event timestamp (UTC).</summary>
    [JsonPropertyName("last_event_utc")]
    public string? LastEventUtc { get; set; }

    /// <summary>Gets or sets the most frequent log categories.</summary>
    [JsonPropertyName("top_categories")]
    public CategoryCount[] TopCategories { get; set; } = Array.Empty<CategoryCount>();

    /// <summary>Gets or sets the distinct exception types observed.</summary>
    [JsonPropertyName("exception_types")]
    public ExceptionTypeSummary[] ExceptionTypes { get; set; } = Array.Empty<ExceptionTypeSummary>();

    /// <summary>Gets or sets an advisory note (for example, log-retention horizon).</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>Gets or sets the logical source.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    #endregion
}

/// <summary>
/// A log category and its occurrence count.
/// </summary>
public sealed class CategoryCount
{
    #region Public Properties

    /// <summary>Gets or sets the category.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>Gets or sets the occurrence count.</summary>
    [JsonPropertyName("count")]
    public long Count { get; set; }

    #endregion
}

/// <summary>
/// An exception type, its occurrence count, and one sample message.
/// </summary>
public sealed class ExceptionTypeSummary
{
    #region Public Properties

    /// <summary>Gets or sets the exception type name.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Gets or sets the occurrence count.</summary>
    [JsonPropertyName("count")]
    public long Count { get; set; }

    /// <summary>Gets or sets one representative (truncated) message.</summary>
    [JsonPropertyName("sample_message")]
    public string? SampleMessage { get; set; }

    #endregion
}
