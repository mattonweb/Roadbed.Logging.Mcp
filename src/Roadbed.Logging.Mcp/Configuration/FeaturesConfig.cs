namespace Roadbed.Logging.Mcp.Configuration;

using Newtonsoft.Json;

/// <summary>
/// Feature toggles. Defaults are conservative; the ad-hoc query tool is off
/// unless an operator explicitly enables it per environment.
/// </summary>
public sealed class FeaturesConfig
{
    #region Public Properties

    /// <summary>
    /// Gets or sets a value indicating whether the guarded <c>run_readonly_query</c>
    /// tool is registered. Defaults to <see langword="false"/>.
    /// </summary>
    [JsonProperty("adHocQuery")]
    public bool AdHocQuery { get; set; }

    #endregion
}
