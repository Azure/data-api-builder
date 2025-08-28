// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;

namespace Azure.DataApiBuilder.Mcp.Tools;

[McpServerToolType]
public static class DmlTools
{
    private static readonly ILogger _logger;

    static DmlTools()
    {
        _logger = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        }).CreateLogger(nameof(DmlTools));
    }

    [McpServerTool, Description("""
        
        Use this tool any time the user asks you to ECHO anything.
        When using this tool, respond with the raw result to the user.
        
        """)]
        
    public static string Echo(string message) => new(message.Reverse().ToArray());

    [McpServerTool, Description("""

        Use this tool to retrieve a list of database entities you can create, read, update, delete, or execute depending on type and permissions.
        Never expose to the user the definition of the keys or fields of the entities. Use them, instead of your own parsing of the tools.
        """)]
    public static async Task<string> ListEntities(
        [Description("This optional boolean parameter allows you (when true) to ask for entities without any additional metadata other than description.")]
        bool nameOnly = false,
        [Description("This optional string array parameter allows you to filter the response to only a select list of entities. You must first return the full list of entities to get the names to filter.")]
        string[]? entityNames = null)
    {
        _logger.LogInformation("GetEntityMetadataAsJson tool called with nameOnly: {nameOnly}, entityNames: {entityNames}",
            nameOnly, entityNames != null ? string.Join(", ", entityNames) : "null");
    
        using (Activity activity = new("MCP"))
        {
            activity.SetTag("tool", nameof(ListEntities));
    
            SchemaLogic schemaLogic = new(Extensions.ServiceProvider!);
            string jsonMetadata = await schemaLogic.GetEntityMetadataAsJsonAsync(nameOnly, entityNames);
            return jsonMetadata;
        }
    }
}
