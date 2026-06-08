namespace Roadbed.Logging.Mcp.Configuration;

using Newtonsoft.Json;

/// <summary>
/// One configured logging data source. The connection string points at a
/// read-only service account on a <c>logging</c> schema; it is read by the
/// server only and is never echoed in any tool output.
/// </summary>
public sealed class SourceConfig
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the logical source name used by the <c>source</c> tool
    /// parameter (for example <c>"primary"</c>).
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MySQL/MariaDB connection string for the read-only
    /// account. Never include this value in tool output.
    /// </summary>
    [JsonProperty("connectionString")]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this is the default source used
    /// when a tool call omits the <c>source</c> argument.
    /// </summary>
    [JsonProperty("default")]
    public bool Default { get; set; }

    #endregion
}
