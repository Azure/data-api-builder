// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp.Tools;

public static partial class Dml
{
    [McpServerTool, Description("""
        Use this tool to retrieve a list of database entities you can create, read, update, delete, or execute depending on type and permissions.
        Never expose to the user the definition of the keys or fields of the entities. Use them, instead of your own parsing of the tools.
        """)]
    public static async Task<string> DescribeEntitiesAsync(
        [Description("""
        This optional boolean parameter allows you (when true) to ask for entities without any additional metadata other than description.
        """)]
        bool nameOnly = false,
        [Description("""
        This optional string array parameter allows you to filter the response to only a select list of entities. You must first return the full list of entities to get the names to filter.
        """)]

        string[]? entityNames = null)
    {
        _logger.LogInformation("GetEntityMetadataAsJson tool called with nameOnly: {nameOnly}, entityNames: {entityNames}",
            nameOnly, entityNames != null ? string.Join(", ", entityNames) : "null");

        using Activity activity = new("MCP");
        activity.SetTag("tool", nameof(DescribeEntitiesAsync));

        SchemaLogic schemaLogic = new(Extensions.ServiceProvider!);
        string jsonMetadata = await schemaLogic.GetEntityMetadataAsJsonAsync(nameOnly, entityNames);
        return jsonMetadata;
    }
}
