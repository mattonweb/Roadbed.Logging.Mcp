namespace Roadbed.Logging.Mcp.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Per-level log counts for a run, keyed by Microsoft.Extensions.Logging level
/// name. Zero-valued levels are omitted from output.
/// </summary>
public sealed class LevelCounts
{
    #region Public Properties

    /// <summary>Gets or sets the Trace-level count.</summary>
    [JsonPropertyName("trace")]
    public long? Trace { get; set; }

    /// <summary>Gets or sets the Debug-level count.</summary>
    [JsonPropertyName("debug")]
    public long? Debug { get; set; }

    /// <summary>Gets or sets the Information-level count.</summary>
    [JsonPropertyName("information")]
    public long? Information { get; set; }

    /// <summary>Gets or sets the Warning-level count.</summary>
    [JsonPropertyName("warning")]
    public long? Warning { get; set; }

    /// <summary>Gets or sets the Error-level count.</summary>
    [JsonPropertyName("error")]
    public long? Error { get; set; }

    /// <summary>Gets or sets the Critical-level count.</summary>
    [JsonPropertyName("critical")]
    public long? Critical { get; set; }

    #endregion
}
