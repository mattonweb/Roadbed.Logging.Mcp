namespace Roadbed.Logging.Mcp.Models;

using System;
using System.Text.Json.Serialization;

/// <summary>
/// One application's footprint, returned by <c>catalog</c> to orient the agent.
/// </summary>
public sealed class CatalogEntry
{
    #region Public Properties

    /// <summary>Gets or sets the application.</summary>
    [JsonPropertyName("application")]
    public string? Application { get; set; }

    /// <summary>Gets or sets the distinct activity types seen.</summary>
    [JsonPropertyName("activity_types")]
    public string[] ActivityTypes { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the top activity keys by run count.</summary>
    [JsonPropertyName("activity_keys")]
    public string[] ActivityKeys { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the run count in the window.</summary>
    [JsonPropertyName("run_count")]
    public long RunCount { get; set; }

    /// <summary>Gets or sets the earliest run timestamp seen (UTC).</summary>
    [JsonPropertyName("first_seen_utc")]
    public string? FirstSeenUtc { get; set; }

    /// <summary>Gets or sets the latest run timestamp seen (UTC).</summary>
    [JsonPropertyName("last_seen_utc")]
    public string? LastSeenUtc { get; set; }

    /// <summary>Gets or sets the logical source.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    #endregion
}
