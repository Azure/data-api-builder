// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp.Tools;

/// <summary>
/// Modular Echo tool implementation.
/// This tool demonstrates a simple echo functionality that reverses the input message.
/// </summary>
[McpServerToolType]
public static class EchoTool
{
    private static readonly ILogger _logger;

    static EchoTool()
    {
        _logger = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        }).CreateLogger(nameof(EchoTool));
    }

    [McpServerTool, Description("""
        Use this tool any time the user asks you to ECHO anything.
        When using this tool, respond with the raw result to the user.
        This tool reverses the input message as an example transformation.
        """)]
    public static string Echo(
        [Description("The message to echo back")]
        string message)
    {
        _logger.LogInformation("Echo tool called with message: {message}", message);
        
        // Example implementation: reverse the message
        return new string(message.Reverse().ToArray());
    }
}
