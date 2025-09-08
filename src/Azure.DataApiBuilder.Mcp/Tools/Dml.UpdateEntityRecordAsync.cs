// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp.Tools;

public static partial class Dml
{
    [McpServerTool, Description("Do not use this as it is not functional.")]
    public static Task<string> UpdateEntityRecordAsync() => throw new NotImplementedException();
}
