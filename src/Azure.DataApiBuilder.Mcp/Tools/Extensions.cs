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

    public static void AddDmlTools(this IServiceCollection services, McpRuntimeOptions? mcpOptions)
    {
        if (mcpOptions?.DmlTools == null || !mcpOptions.DmlTools.Enabled)
        {
            return;
        }

        HashSet<string> DmlToolNames = new();

        // Check each DML tool property and add to the set if enabled
        if (mcpOptions.DmlTools.DescribeEntities)
        {
            DmlToolNames.Add("DescribeEntities");
        }

        if (mcpOptions.DmlTools.CreateRecord)
        {
            DmlToolNames.Add("CreateRecord");
        }

        if (mcpOptions.DmlTools.ReadRecord)
        {
            DmlToolNames.Add("ReadRecord");
        }

        if (mcpOptions.DmlTools.UpdateRecord)
        {
            DmlToolNames.Add("UpdateRecord");
        }

        if (mcpOptions.DmlTools.DeleteRecord)
        {
            DmlToolNames.Add("DeleteRecord");
        }

        if (mcpOptions.DmlTools.ExecuteRecord)
        {
            DmlToolNames.Add("ExecuteRecord");
        }

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
