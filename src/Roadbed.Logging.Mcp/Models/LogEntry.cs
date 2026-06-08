namespace Roadbed.Logging.Mcp.Models;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// One MEL log row. <c>level</c> is a name; <c>message</c>/<c>exception</c> are
/// truncated unless <c>full=true</c>; <c>properties</c> is parsed JSON.
/// </summary>
public sealed class LogEntry
{
    #region Public Properties

    /// <summary>Gets or sets the log row id.</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Gets or sets the event timestamp (UTC, the log partition key).</summary>
    [JsonPropertyName("event_time_utc")]
    public string EventTimeUtc { get; set; } = string.Empty;

    /// <summary>Gets or sets the level name.</summary>
    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    /// <summary>Gets or sets the log category.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>Gets or sets the event id.</summary>
    [JsonPropertyName("event_id")]
    public int? EventId { get; set; }

    /// <summary>Gets or sets the event name.</summary>
    [JsonPropertyName("event_name")]
    public string? EventName { get; set; }

    /// <summary>Gets or sets the rendered message (possibly truncated).</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>Gets or sets a value indicating whether <see cref="Message"/> was truncated.</summary>
    [JsonPropertyName("message_truncated")]
    public bool? MessageTruncated { get; set; }

    /// <summary>Gets or sets the message template.</summary>
    [JsonPropertyName("message_template")]
    public string? MessageTemplate { get; set; }

    /// <summary>Gets or sets the parsed structured <c>properties</c> JSON.</summary>
    [JsonPropertyName("properties")]
    public JsonElement? Properties { get; set; }

    /// <summary>Gets or sets the exception type name.</summary>
    [JsonPropertyName("exception_type")]
    public string? ExceptionType { get; set; }

    /// <summary>Gets or sets the exception text (possibly truncated).</summary>
    [JsonPropertyName("exception")]
    public string? Exception { get; set; }

    /// <summary>Gets or sets a value indicating whether <see cref="Exception"/> was truncated.</summary>
    [JsonPropertyName("exception_truncated")]
    public bool? ExceptionTruncated { get; set; }

    /// <summary>Gets or sets the owning activity ULID (pivot to <c>activity_get</c>).</summary>
    [JsonPropertyName("activity_id")]
    public string? ActivityId { get; set; }

    /// <summary>Gets or sets the W3C trace id.</summary>
    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }

    /// <summary>Gets or sets the W3C span id.</summary>
    [JsonPropertyName("span_id")]
    public string? SpanId { get; set; }

    /// <summary>Gets or sets the originating application.</summary>
    [JsonPropertyName("application")]
    public string? Application { get; set; }

    /// <summary>Gets or sets the host that emitted the log.</summary>
    [JsonPropertyName("host")]
    public string? Host { get; set; }

    /// <summary>Gets or sets the logical source the row came from.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    #endregion
}
