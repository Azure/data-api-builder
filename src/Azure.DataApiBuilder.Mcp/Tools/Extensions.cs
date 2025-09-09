// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp.Tools;

public static class Extensions
{
    public static IServiceProvider? ServiceProvider { get; set; }

    public static void AddDmlTools(this IServiceCollection services, McpOptions mcpOptions)
    {
        HashSet<string> DmlToolNames = mcpOptions.DmlTools
            .Select(x => x.ToString()).ToHashSet();

        IEnumerable<MethodInfo> methods = typeof(DmlTools).GetMethods()
            .Where(method => DmlToolNames.Contains(method.Name));

        foreach (MethodInfo method in methods)
        {
            AddTool(services, method);
        }

        AddTool(services, typeof(DmlTools).GetMethod("Echo")!);
    }

    private static void AddTool(IServiceCollection services, MethodInfo method)
    {
        Func<IServiceProvider, McpServerTool> factory = (services) =>
        {
            ServiceProvider ??= services;

            McpServerTool tool = McpServerTool
                .Create(method, options: new()
                {
                    Services = services,
                    SerializerOptions = default
                });
            return tool;
        };
        _ = services.AddSingleton(factory);
    }
}
