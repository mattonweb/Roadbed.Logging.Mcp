namespace Roadbed.Logging.Mcp.Models;

using System;
using System.Text.Json.Serialization;

/// <summary>
/// Result of the guarded ad-hoc <c>run_readonly_query</c> tool: column names and
/// row tuples, with a truncation flag when the row cap was hit.
/// </summary>
public sealed class AdHocQueryResult
{
    #region Public Properties

    /// <summary>Gets or sets the result column names in order.</summary>
    [JsonPropertyName("columns")]
    public string[] Columns { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the result rows; each row is an ordered value array.</summary>
    [JsonPropertyName("rows")]
    public object?[][] Rows { get; set; } = Array.Empty<object?[]>();

    /// <summary>Gets or sets the returned row count.</summary>
    [JsonPropertyName("row_count")]
    public int RowCount { get; set; }

    /// <summary>Gets or sets a value indicating whether the row cap truncated the result.</summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }

    /// <summary>Gets or sets the logical source.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    #endregion
}
