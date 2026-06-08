namespace Roadbed.Logging.Mcp.Tools;

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using global::Roadbed.Logging.Mcp.Data;
using global::Roadbed.Logging.Mcp.Lib;

/// <summary>
/// Orientation tools that help the agent learn what exists in the fleet.
/// </summary>
[McpServerToolType]
public static class OrientationTools
{
    #region Public Methods

    /// <summary>
    /// Lists, per application, the activity types and top activity keys seen in a
    /// window, to orient the analyst and seed its knowledge base.
    /// </summary>
    /// <param name="repository">The injected repository.</param>
    /// <param name="registry">The injected source registry.</param>
    /// <param name="source">Optional source name.</param>
    /// <param name="since">Optional ISO-8601 window start.</param>
    /// <param name="until">Optional ISO-8601 window end.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Compact JSON catalog entries.</returns>
    [McpServerTool(Name = "catalog")]
    [Description("Per-application footprint: activity types, top activity keys, run count, and first/last-seen "
        + "timestamps. Filters on created_on; default window is the last 30 days (activity retention ~12 months).")]
    public static Task<string> Catalog(
        LoggingRepository repository,
        ISourceRegistry registry,
        [Description("Logical source name. Defaults to the configured default source.")]
        string? source = null,
        [Description("Window start, ISO-8601 UTC (filters created_on). Default: 30 days ago.")]
        string? since = null,
        [Description("Window end, ISO-8601 UTC (filters created_on). Default: now.")]
        string? until = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSupport.Guard(async () =>
        {
            var (factory, name) = ToolSupport.ResolveSource(registry, source);
            var (windowStart, windowEnd) = ToolSupport.ResolveWindow(since, until, defaultDays: 30);
            var entries = await repository.CatalogAsync(
                factory, name, windowStart, windowEnd, topKeys: 10, cancellationToken);
            return Json.Serialize(entries);
        });
    }

    #endregion
}
