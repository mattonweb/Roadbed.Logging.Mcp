namespace Roadbed.Logging.Mcp.Data;

using System.Collections.Generic;
using global::Roadbed.Data;

/// <summary>
/// Resolves a logical source name to its read-only connection factory. One
/// <see cref="IDataConnectionFactory"/> is built per configured source at
/// startup; tools resolve <c>source ?? default</c> against this registry rather
/// than injecting a single factory from DI (the source list is dynamic).
/// </summary>
public interface ISourceRegistry
{
    #region Properties

    /// <summary>
    /// Gets the name of the default source used when a call omits <c>source</c>.
    /// </summary>
    string DefaultSourceName { get; }

    /// <summary>
    /// Gets the names of all configured sources.
    /// </summary>
    IReadOnlyCollection<string> SourceNames { get; }

    #endregion

    #region Methods

    /// <summary>
    /// Resolves a requested source name (or the default when null/blank) to its
    /// connection factory.
    /// </summary>
    /// <param name="requested">The requested source name, or null for the default.</param>
    /// <param name="factory">The resolved connection factory when successful.</param>
    /// <param name="resolvedName">The resolved source name when successful.</param>
    /// <returns><see langword="true"/> when the source is known.</returns>
    bool TryResolve(string? requested, out IDataConnectionFactory factory, out string resolvedName);

    #endregion
}
