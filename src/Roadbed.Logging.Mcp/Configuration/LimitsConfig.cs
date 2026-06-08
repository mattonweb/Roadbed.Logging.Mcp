namespace Roadbed.Logging.Mcp.Configuration;

using Newtonsoft.Json;

/// <summary>
/// Hard ceilings that keep the agent from accidentally scanning everything.
/// Per-tool <c>limit</c> arguments are clamped to these values.
/// </summary>
public sealed class LimitsConfig
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the maximum number of activity rows any single list/history
    /// tool may return.
    /// </summary>
    [JsonProperty("maxActivities")]
    public int MaxActivities { get; set; } = 200;

    /// <summary>
    /// Gets or sets the maximum number of log rows any single log tool may
    /// return.
    /// </summary>
    [JsonProperty("maxLogRows")]
    public int MaxLogRows { get; set; } = 500;

    /// <summary>
    /// Gets or sets the character count at which free-text fields
    /// (<c>error</c>, <c>message</c>, <c>exception</c>) are truncated.
    /// </summary>
    [JsonProperty("textTruncateChars")]
    public int TextTruncateChars { get; set; } = 500;

    #endregion
}
