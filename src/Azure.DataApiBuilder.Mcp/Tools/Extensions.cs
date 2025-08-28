// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp.Tools;

public static class Extensions
{
    public static void AddDmlTools(this IServiceCollection services, McpOptions mcpOptions)
    {
        HashSet<string> DmlToolNames = mcpOptions.DmlTools
            .Select(x => x.ToString()).ToHashSet();

        foreach (MethodInfo method in typeof(DmlTools).GetMethods())
        {
            if (DmlToolNames.Contains(method.Name))
            {
                services.AddMcpTool(method);
            }
        }
    }

    public static void AddMcpTool(this IServiceCollection services, MethodInfo method)
    {
        Func<IServiceProvider, McpServerTool> factory = (services) => McpServerTool
            .Create(method, options: new() { Services = services, SerializerOptions = default });
        _ = services.AddSingleton(factory);
    }
}
