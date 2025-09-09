// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

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

    public static async Task<string> DescribeEntities(
        [Description("This optional boolean parameter allows you (when true) to ask for entities without any additional metadata other than description.")]
        bool nameOnly = false,
        [Description("This optional string array parameter allows you to filter the response to only a select list of entities. You must first return the full list of entities to get the names to filter.")]
        string[]? entityNames = null)
    {
        _logger.LogInformation("GetEntityMetadataAsJson tool called with nameOnly: {nameOnly}, entityNames: {entityNames}",
            nameOnly, entityNames != null ? string.Join(", ", entityNames) : "null");

        using (Activity activity = new("MCP"))
        {
            activity.SetTag("tool", nameof(DescribeEntities));
            SchemaLogic schemaLogic = new(Extensions.ServiceProvider!);
            string jsonMetadata = await schemaLogic.GetEntityMetadataAsJsonAsync(nameOnly, entityNames);
            return jsonMetadata;
        }
    }
}
