namespace Roadbed.Logging.Mcp;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Roadbed.Logging.Mcp.Configuration;
using Roadbed.Logging.Mcp.Data;
using Roadbed.Logging.Mcp.Tools;

// Read-only MCP server over the Roadbed.Logging schema. stdio transport: stdout
// is the protocol channel, so ALL diagnostics are routed to stderr.
internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        McpConfig config;
        try
        {
            config = ConfigLoader.Load();
        }
        catch (InvalidOperationException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 1;
        }

        var builder = Host.CreateApplicationBuilder(args);

        // Route every log to stderr; writing to stdout would corrupt the MCP stream.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton<ISourceRegistry, SourceRegistry>();
        builder.Services.AddSingleton<LoggingRepository>();

        // Explicit tool list so the optional ad-hoc tool is registered only when enabled.
        var toolTypes = new List<Type>
        {
            typeof(OrientationTools),
            typeof(TriageTools),
            typeof(ActivityTools),
            typeof(LogTools),
        };

        if (config.Features.AdHocQuery)
        {
            toolTypes.Add(typeof(AdHocTools));
        }

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools((IEnumerable<Type>)toolTypes);

        await builder.Build().RunAsync();
        return 0;
    }
}
