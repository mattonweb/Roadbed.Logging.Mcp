namespace Roadbed.Logging.Mcp.Configuration;

using Newtonsoft.Json;

/// <summary>
/// Default look-back windows (in days) applied when a tool's <c>since</c>/<c>until</c>
/// arguments are omitted, so no call scans the full retention range by accident.
/// </summary>
public sealed class DefaultWindowConfig
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the default window for list/drill activity tools.
    /// </summary>
    [JsonProperty("lists")]
    public int Lists { get; set; } = 7;

    /// <summary>
    /// Gets or sets the default window for per-run log tools.
    /// </summary>
    [JsonProperty("logs")]
    public int Logs { get; set; } = 1;

    /// <summary>
    /// Gets or sets the default window for cross-run history (capped to the
    /// ~12-month activity retention).
    /// </summary>
    [JsonProperty("history")]
    public int History { get; set; } = 90;

    /// <summary>
    /// Gets or sets the default window for fleet overview and catalog rollups.
    /// </summary>
    [JsonProperty("overview")]
    public int Overview { get; set; } = 7;

    #endregion
}
