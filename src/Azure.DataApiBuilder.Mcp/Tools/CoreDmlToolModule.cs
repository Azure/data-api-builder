// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp.Tools;

/// <summary>
/// Core DML (Data Manipulation Language) tools module.
/// This module provides the core database-related tools for the MCP server.
/// </summary>
public class CoreDmlToolModule : IToolModule
{
    public string ModuleName => "Core DML Tools";

    public void RegisterTools(IServiceCollection services)
    {
        // Register Echo tool
        RegisterToolFromMethod(services, typeof(EchoTool), nameof(EchoTool.Echo));
        
        // Register ListEntities tool
        RegisterToolFromMethod(services, typeof(ListEntitiesTool), nameof(ListEntitiesTool.ListEntities));
    }

    private static void RegisterToolFromMethod(IServiceCollection services, Type toolType, string methodName)
    {
        MethodInfo? method = toolType.GetMethod(methodName);
        if (method == null)
        {
            throw new InvalidOperationException($"Method {methodName} not found on type {toolType.Name}");
        }

        Func<IServiceProvider, McpServerTool> factory = (serviceProvider) =>
        {
            // Set the service provider for tools that need it
            Extensions.ServiceProvider ??= serviceProvider;

            McpServerTool tool = McpServerTool.Create(method, options: new()
            {
                Services = serviceProvider,
                SerializerOptions = default
            });
            return tool;
        };

        services.AddSingleton(factory);
    }
}
