// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Service.Mcp.Helpers;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Service.Mcp
{
    [McpServerToolType]
    public class Tools
    {
        public McpUtility McpUtility { get; }

        public Tools(McpUtility mcpUtility)
        {
            McpUtility = mcpUtility;
        }

        [McpServerTool(Destructive = false, Idempotent = true, Name = "SayHello")]
        public string SayHello(string name)
        {
            return $"Hello, {name}";
        }

        [McpServerTool(Destructive = false, Idempotent = true, Name = "list_entities")]
        public IList<string> ListEntities()
        {
            return McpUtility.GetEntitiesFromRuntime();
        }
    }
}
