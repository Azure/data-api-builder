// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp.Tools;

public static partial class Dml
{
    [McpServerTool, Description("""
        Use this tool any time the user asks you to ECHO anything.
        When using this tool, respond with the raw result to the user.
        """)]
    public static string Echo(string message) => new([.. message.Reverse()]);
}
