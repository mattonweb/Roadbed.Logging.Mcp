namespace Roadbed.Logging.Mcp.Tools;

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using global::Roadbed.Logging.Mcp.Configuration;
using global::Roadbed.Logging.Mcp.Data;
using global::Roadbed.Logging.Mcp.Lib;
using global::Roadbed.Logging.Mcp.Models;

/// <summary>
/// The optional, config-gated ad-hoc read-only query tool. The host registers
/// this type explicitly only when <c>features.adHocQuery = true</c> (it is not
/// part of the always-on tool list), so it does not appear otherwise.
/// </summary>
[McpServerToolType]
public static class AdHocTools
{
    #region Public Methods

    /// <summary>
    /// Executes a single guarded read-only <c>SELECT</c>/<c>WITH</c> query.
    /// </summary>
    /// <param name="repository">The injected repository.</param>
    /// <param name="registry">The injected source registry.</param>
    /// <param name="config">The injected configuration.</param>
    /// <param name="sql">The query text.</param>
    /// <param name="maxRows">Optional row cap.</param>
    /// <param name="source">Optional source name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Compact JSON columns/rows, or a structured error.</returns>
    [McpServerTool(Name = "run_readonly_query")]
    [Description("Guarded ad-hoc read-only query. Accepts a single SELECT/WITH statement (no chaining, comments, "
        + "or DDL/DML), wraps it in a LIMIT, and returns columns + row tuples. The read-only DB account is the "
        + "authoritative backstop.")]
    public static Task<string> RunReadonlyQuery(
        LoggingRepository repository,
        ISourceRegistry registry,
        McpConfig config,
        [Description("A single SELECT or WITH (CTE) statement.")]
        string sql,
        [Description("Max rows to return (default 200, capped by server limits).")]
        int? maxRows = null,
        [Description("Logical source name. Defaults to the configured default source.")]
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        return ToolSupport.Guard(async () =>
        {
            if (!config.Features.AdHocQuery)
            {
                return ToolError.ToJson("The run_readonly_query feature is disabled.", "sql");
            }

            var (factory, name) = ToolSupport.ResolveSource(registry, source);
            var cap = ToolSupport.ClampLimit(maxRows, defaultValue: 200, config.Limits.MaxLogRows);
            var (ok, wrapped, error) = AdHocSqlValidator.Validate(sql, cap);
            if (!ok)
            {
                return ToolError.ToJson(error, "sql");
            }

            var result = await repository.RunReadonlyQueryAsync(factory, name, wrapped, cap, cancellationToken);
            return Json.Serialize(result);
        });
    }

    #endregion
}
