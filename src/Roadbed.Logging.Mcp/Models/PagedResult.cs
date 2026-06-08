namespace Roadbed.Logging.Mcp.Models;

using System;
using System.Text.Json.Serialization;

/// <summary>
/// A page of results plus an opaque keyset cursor for the next page and an
/// optional advisory note.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class PagedResult<T>
{
    #region Public Properties

    /// <summary>Gets or sets the page items.</summary>
    [JsonPropertyName("items")]
    public T[] Items { get; set; } = Array.Empty<T>();

    /// <summary>Gets or sets the cursor for the next page (null when exhausted).</summary>
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }

    /// <summary>Gets or sets an advisory note (for example, retention horizon).</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>Gets or sets the logical source.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    #endregion
}
