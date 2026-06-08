namespace Roadbed.Logging.Mcp.Lib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Shared System.Text.Json serialization for the MCP boundary. Tools return
/// compact JSON strings produced here so output is null-omitting and dense,
/// independent of the SDK's default serializer settings. (Newtonsoft is used for
/// Roadbed-side concerns such as config; this is the protocol boundary.)
/// </summary>
public static class Json
{
    #region Private Fields

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    #endregion

    #region Public Methods

    /// <summary>
    /// Serializes a value to a compact JSON string, omitting null members.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The compact JSON representation.</returns>
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    /// <summary>
    /// Parses a JSON-column string into a detached <see cref="JsonElement"/> for
    /// passthrough over the MCP boundary (avoids double-encoding). When the text
    /// is not valid JSON it is preserved as a JSON string value.
    /// </summary>
    /// <param name="raw">The raw JSON column text.</param>
    /// <returns>The parsed element, or <see langword="null"/> when the input is null/blank.</returns>
    public static JsonElement? ParseColumn(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(raw, Options);
        }
    }

    #endregion
}
