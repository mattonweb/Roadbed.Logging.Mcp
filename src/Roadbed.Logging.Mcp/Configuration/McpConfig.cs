namespace Roadbed.Logging.Mcp.Configuration;

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// Root configuration for the server, loaded from a file outside the agent's
/// workspace (see <see cref="ConfigLoader"/>). Holds the configured sources,
/// limits, feature toggles, and default windows.
/// </summary>
public sealed class McpConfig
{
    #region Public Properties

    /// <summary>
    /// Gets the configured logging sources. At least one is required; exactly
    /// one must be marked default (or there must be a single source).
    /// </summary>
    [JsonProperty("sources")]
    public IList<SourceConfig> Sources { get; } = new List<SourceConfig>();

    /// <summary>
    /// Gets or sets the result-size ceilings.
    /// </summary>
    [JsonProperty("limits")]
    public LimitsConfig Limits { get; set; } = new LimitsConfig();

    /// <summary>
    /// Gets or sets the feature toggles.
    /// </summary>
    [JsonProperty("features")]
    public FeaturesConfig Features { get; set; } = new FeaturesConfig();

    /// <summary>
    /// Gets or sets the default look-back windows in days.
    /// </summary>
    [JsonProperty("defaultWindowDays")]
    public DefaultWindowConfig DefaultWindowDays { get; set; } = new DefaultWindowConfig();

    #endregion

    #region Public Methods

    /// <summary>
    /// Validates structural invariants. Messages never contain connection
    /// strings or credentials.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no sources are configured, names are blank or duplicated,
    /// a connection string is missing, or the default source is ambiguous.
    /// </exception>
    public void Validate()
    {
        if (this.Sources.Count == 0)
        {
            throw new InvalidOperationException(
                "Configuration error: at least one source must be configured under 'sources'.");
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in this.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Name))
            {
                throw new InvalidOperationException(
                    "Configuration error: every source requires a non-empty 'name'.");
            }

            if (!seenNames.Add(source.Name))
            {
                throw new InvalidOperationException(
                    $"Configuration error: duplicate source name '{source.Name}'.");
            }

            if (string.IsNullOrWhiteSpace(source.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"Configuration error: source '{source.Name}' is missing 'connectionString'.");
            }
        }

        var defaultCount = this.Sources.Count(s => s.Default);
        if (defaultCount > 1)
        {
            throw new InvalidOperationException(
                "Configuration error: more than one source is marked 'default: true'.");
        }

        if (defaultCount == 0 && this.Sources.Count > 1)
        {
            throw new InvalidOperationException(
                "Configuration error: multiple sources are configured but none is marked 'default: true'.");
        }
    }

    /// <summary>
    /// Resolves the default source name (the one flagged default, or the single
    /// configured source).
    /// </summary>
    /// <returns>The default source name.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no default can be resolved.</exception>
    public string ResolveDefaultSourceName()
    {
        var flagged = this.Sources.FirstOrDefault(s => s.Default);
        if (flagged is not null)
        {
            return flagged.Name;
        }

        if (this.Sources.Count == 1)
        {
            return this.Sources[0].Name;
        }

        throw new InvalidOperationException("Configuration error: no default source could be resolved.");
    }

    #endregion
}
