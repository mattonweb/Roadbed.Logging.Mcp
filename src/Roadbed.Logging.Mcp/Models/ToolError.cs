namespace Roadbed.Logging.Mcp.Models;

using System.Text.Json.Serialization;
using Roadbed.Logging.Mcp.Lib;

/// <summary>
/// Structured error payload returned by tools for invalid arguments or unknown
/// sources. Never carries connection strings or credentials.
/// </summary>
public sealed class ToolError
{
    #region Public Properties

    /// <summary>Gets or sets the human-readable error message.</summary>
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    /// <summary>Gets or sets the offending argument name, when applicable.</summary>
    [JsonPropertyName("argument")]
    public string? Argument { get; set; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Builds a compact JSON error string.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="argument">The offending argument name, when applicable.</param>
    /// <returns>A compact JSON representation of the error.</returns>
    public static string ToJson(string message, string? argument = null)
    {
        return Json.Serialize(new ToolError { Error = message, Argument = argument });
    }

    #endregion
}
