// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
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

    [McpServerTool]
    public static string Echo(string message)
    {
        _logger.LogInformation("Echo tool called with message: {message}", message);
        using (Activity activity = new("MCP"))
        {
            activity.SetTag("tool", nameof(Echo));
            return message;
        }
    }
}
