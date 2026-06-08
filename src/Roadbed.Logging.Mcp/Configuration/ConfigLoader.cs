namespace Roadbed.Logging.Mcp.Configuration;

using System;
using System.IO;
using Newtonsoft.Json;

/// <summary>
/// Loads <see cref="McpConfig"/> from a file outside the agent's workspace:
/// <c>.Roadbed.Logging.Mcp</c> in the current user's home directory
/// (<c>C:\Users\{user}\.Roadbed.Logging.Mcp</c>). No credentials live in the
/// repository or in <c>.mcp.json</c>.
/// </summary>
public static class ConfigLoader
{
    #region Private Fields

    private const string ConfigFileName = ".Roadbed.Logging.Mcp";

    #endregion

    #region Public Methods

    /// <summary>
    /// Resolves the configuration file path: <c>.Roadbed.Logging.Mcp</c> in the
    /// current user's home directory.
    /// </summary>
    /// <returns>The resolved absolute path (which may not yet exist).</returns>
    public static string ResolvePath()
    {
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userHome, ConfigFileName);
    }

    /// <summary>
    /// Loads and validates the configuration from the resolved path.
    /// </summary>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the file is missing, cannot be parsed, or fails validation.
    /// </exception>
    public static McpConfig Load()
    {
        return LoadFromFile(ResolvePath());
    }

    /// <summary>
    /// Loads and validates the configuration from an explicit path.
    /// </summary>
    /// <param name="path">The configuration file path.</param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the file is missing, cannot be parsed, or fails validation.
    /// </exception>
    public static McpConfig LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Configuration file not found. Create '{ConfigFileName}' in your home directory: '{path}'.");
        }

        var json = File.ReadAllText(path);

        McpConfig? config;
        try
        {
            config = JsonConvert.DeserializeObject<McpConfig>(json);
        }
        catch (JsonException ex)
        {
            // Deliberately do not surface the file contents; they hold credentials.
            throw new InvalidOperationException(
                $"Configuration file at '{path}' is not valid JSON: {ex.Message}");
        }

        if (config is null)
        {
            throw new InvalidOperationException(
                $"Configuration file at '{path}' deserialized to nothing.");
        }

        config.Validate();
        return config;
    }

    #endregion
}
