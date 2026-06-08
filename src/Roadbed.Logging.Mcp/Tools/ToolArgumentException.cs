namespace Roadbed.Logging.Mcp.Tools;

using System;

/// <summary>
/// Signals an invalid tool argument. The tool boundary catches this and returns
/// a structured <c>ToolError</c> naming the offending argument.
/// </summary>
public sealed class ToolArgumentException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolArgumentException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="argument">The offending argument name, when applicable.</param>
    public ToolArgumentException(string message, string? argument)
        : base(message)
    {
        this.Argument = argument;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolArgumentException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ToolArgumentException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolArgumentException"/> class.
    /// </summary>
    public ToolArgumentException()
    {
    }

    /// <summary>
    /// Gets the offending argument name, when applicable.
    /// </summary>
    public string? Argument { get; }
}
