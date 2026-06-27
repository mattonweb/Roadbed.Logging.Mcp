namespace Roadbed.Logging.Mcp.Data;

using System;
using System.Collections.Generic;
using global::Roadbed;
using global::Roadbed.Data;
using global::Roadbed.Data.MySql;
using global::Roadbed.Logging.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;

/// <summary>
/// Builds and holds one read-only MySQL/MariaDB connection factory per configured
/// source. Connection strings are hardened (LOAD DATA LOCAL disabled, a default
/// command timeout enforced) before use; the read-only DB account remains the
/// authoritative backstop.
/// </summary>
public sealed class SourceRegistry : BaseClassWithLogging, ISourceRegistry
{
    #region Private Fields

    private const uint DefaultCommandTimeoutSeconds = 30;

    private const uint ConnectTimeoutSeconds = 10;

    private readonly Dictionary<string, IDataConnectionFactory> _factories =
        new Dictionary<string, IDataConnectionFactory>(StringComparer.OrdinalIgnoreCase);

    private readonly string _defaultSourceName;

    #endregion

    #region Public Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceRegistry"/> class.
    /// </summary>
    /// <param name="config">The validated server configuration.</param>
    /// <param name="logger">The logger (diagnostics go to stderr).</param>
    public SourceRegistry(McpConfig config, ILogger<SourceRegistry> logger)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(config);

        this._defaultSourceName = config.ResolveDefaultSourceName();

        foreach (var source in config.Sources)
        {
            var hardened = Harden(source.Name, source.ConnectionString);
            var connection = new DataConnecionString(DataConnectionStringType.MySQL, hardened);
            this._factories[source.Name] = new MySqlConnectionFactory(connection);
        }

        this.LogInformation(
            "Registered {SourceCount} logging source(s); default is {DefaultSource}.",
            this._factories.Count,
            this._defaultSourceName);
    }

    #endregion

    #region Public Properties

    /// <inheritdoc/>
    public string DefaultSourceName => this._defaultSourceName;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SourceNames => this._factories.Keys;

    #endregion

    #region Public Methods

    /// <inheritdoc/>
    public bool TryResolve(string? requested, out IDataConnectionFactory factory, out string resolvedName)
    {
        resolvedName = string.IsNullOrWhiteSpace(requested) ? this._defaultSourceName : requested.Trim();
        return this._factories.TryGetValue(resolvedName, out factory!);
    }

    #endregion

    #region Private Methods

    private static string Harden(string sourceName, string rawConnectionString)
    {
        try
        {
            var builder = new MySqlConnectionStringBuilder(rawConnectionString)
            {
                AllowLoadLocalInfile = false,

                // Read every id column as a raw string instead of letting the connector coerce it
                // to a Guid. The id and activity_id columns are fixed-width 36-char text holding
                // UUIDv7 values. None keeps the reader independent of connector guid-coercion: any
                // value reads as text rather than failing at the connector before the row reaches
                // Dapper, so a non-canonical id never crashes a triage query. IdColumn then
                // canonicalizes, and still handles a real Guid from a source backed by a native
                // uuid type.
                GuidFormat = MySqlGuidFormat.None,
            };

            if (builder.DefaultCommandTimeout == 0)
            {
                builder.DefaultCommandTimeout = DefaultCommandTimeoutSeconds;
            }

            // Bound connect attempts so an unreachable source fails fast (reads run without retries).
            if (builder.ConnectionTimeout == 0 || builder.ConnectionTimeout > ConnectTimeoutSeconds)
            {
                builder.ConnectionTimeout = ConnectTimeoutSeconds;
            }

            return builder.ConnectionString;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // Never surface the connection string itself.
            throw new InvalidOperationException(
                $"Configuration error: source '{sourceName}' has an unparseable connection string.",
                ex);
        }
    }

    #endregion
}
