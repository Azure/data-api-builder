// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp.Tools;

[McpServerToolType]
public static partial class Dml
{
    private static readonly ILogger _logger;

    static Dml()
    {
        _logger = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        }).CreateLogger(nameof(Dml));
    }
}
