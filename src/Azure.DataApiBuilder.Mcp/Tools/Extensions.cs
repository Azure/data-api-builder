// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp.Tools;

internal static class Extensions
{
    public static IServiceProvider? ServiceProvider { get; set; }

    public static IServiceCollection AddDmlTools(this IServiceCollection services, McpOptions mcpOptions)
    {
        HashSet<string> DmlToolNames = mcpOptions.DmlTools
            .Select(x => x.ToString()).ToHashSet();

        IEnumerable<MethodInfo> methods = typeof(Dml).GetMethods()
            .Where(method => DmlToolNames.Contains(method.Name));

        foreach (MethodInfo method in methods)
        {
            AddTool(services, method);
        }

        // this is special during development
        AddTool(services, typeof(Dml).GetMethod("Echo") ?? throw new Exception("Echo method not found"));

        return services;
    }

    private static void AddTool(IServiceCollection services, MethodInfo method)
    {
        McpServerTool factory(IServiceProvider services)
        {
            ServiceProvider ??= services;

            McpServerTool tool = McpServerTool
                .Create(method, options: new()
                {
                    Services = services,
                    SerializerOptions = default
                });
            return tool;
        }

        _ = services.AddSingleton(factory);
    }
}
