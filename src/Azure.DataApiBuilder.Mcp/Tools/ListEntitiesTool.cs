// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp.Tools;

/// <summary>
/// Modular ListEntities tool implementation.
/// This tool retrieves database entity metadata based on configuration and user parameters.
/// </summary>
[McpServerToolType]
public static class ListEntitiesTool
{
    private static readonly ILogger _logger;

    static ListEntitiesTool()
    {
        _logger = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        }).CreateLogger(nameof(ListEntitiesTool));
    }

    [McpServerTool, Description("""
        Use this tool to retrieve a list of database entities you can create, read, update, delete, or execute depending on type and permissions.
        Never expose to the user the definition of the keys or fields of the entities. Use them, instead of your own parsing of the tools.
        This tool provides comprehensive metadata about available database entities.
        """)]
    public static async Task<string> ListEntities(
        [Description("This optional boolean parameter allows you (when true) to ask for entities without any additional metadata other than description.")]
        bool nameOnly = false,
        [Description("This optional string array parameter allows you to filter the response to only a select list of entities. You must first return the full list of entities to get the names to filter.")]
        string[]? entityNames = null)
    {
        _logger.LogInformation("ListEntities tool called with nameOnly: {nameOnly}, entityNames: {entityNames}",
            nameOnly, entityNames != null ? string.Join(", ", entityNames) : "null");

        using Activity activity = new("MCP");
        activity.SetTag("tool", nameof(ListEntities));

        // Get the service provider from the registered tools extension
        IServiceProvider? serviceProvider = Extensions.ServiceProvider;
        if (serviceProvider == null)
        {
            _logger.LogError("Service provider not available for ListEntities tool");
            return "{}"; // Return empty JSON object
        }

        SchemaLogic schemaLogic = new(serviceProvider);
        string jsonMetadata = await schemaLogic.GetEntityMetadataAsJsonAsync(nameOnly, entityNames);
        return jsonMetadata;
    }
}
