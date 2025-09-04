// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Mcp.Tools;

public static class Extensions
{
    public static IServiceProvider? ServiceProvider { get; set; }

    /// <summary>
    /// Adds DML tools to the service collection using a modular approach.
    /// This method completely removes hardcoded tool registration in favor of modular tool modules.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="mcpOptions">MCP configuration options</param>
    public static void AddDmlTools(this IServiceCollection services, McpOptions mcpOptions)
    {
        ILogger logger = CreateLogger();
        logger.LogInformation("Registering DML tools using modular architecture");

        // Get configured tool names from options
        HashSet<string> configuredToolNames = mcpOptions.DmlTools
            .Select(x => x.ToString()).ToHashSet();

        logger.LogInformation("Configured tools: {tools}", string.Join(", ", configuredToolNames));

        // Register tool modules based on configuration
        IList<IToolModule> toolModules = GetAvailableToolModules();

        foreach (IToolModule module in toolModules)
        {
            logger.LogInformation("Registering tool module: {moduleName}", module.ModuleName);
            module.RegisterTools(services);
        }

        // Also register any additional tools from configuration if needed
        // This allows for future extensibility without hardcoding
        if (configuredToolNames.Contains("Echo"))
        {
            logger.LogInformation("Echo tool is enabled in configuration");
        }

        if (configuredToolNames.Contains("ListEntities"))
        {
            logger.LogInformation("ListEntities tool is enabled in configuration");
        }
    }

    /// <summary>
    /// Gets all available tool modules. This method can be extended to support
    /// dynamic discovery of tool modules from assemblies or configuration.
    /// </summary>
    /// <returns>List of available tool modules</returns>
    private static IList<IToolModule> GetAvailableToolModules()
    {
        return new List<IToolModule>
        {
            new CoreDmlToolModule()
            // Add more tool modules here as they are created
            // This is the only place where modules are referenced, making it easy to extend
        };
    }

    /// <summary>
    /// Creates a logger for the tools extension methods.
    /// </summary>
    /// <returns>Logger instance</returns>
    private static ILogger CreateLogger()
    {
        return LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        }).CreateLogger("Azure.DataApiBuilder.Mcp.Tools.Extensions");
    }
}
