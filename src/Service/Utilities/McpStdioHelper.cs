using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Azure.DataApiBuilder.Service.Utilities
{
    /// <summary>
    /// Helper methods for configuring and running MCP in stdio mode.
    /// </summary>
    internal static class McpStdioHelper
    {
        /// <summary>
        /// Determines if MCP stdio mode should be run based on command line arguments.
        /// </summary>
        /// <param name="args"> The command line arguments.</param>
        /// <param name="mcpRole"> The role for MCP stdio mode. When this method returns true, the role is guaranteed to be non-null.</param>
        /// <returns>True when MCP stdio mode should be enabled; otherwise false.</returns>
        public static bool ShouldRunMcpStdio(string[] args, [NotNullWhen(true)] out string? mcpRole)
        {
            mcpRole = null;

            bool runMcpStdio = Array.Exists(
                args,
                a => string.Equals(a, "--mcp-stdio", StringComparison.OrdinalIgnoreCase));

            if (!runMcpStdio)
            {
                return false;
            }

            string? roleArg = Array.Find(
                args,
                a => a != null && a.StartsWith("role:", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(roleArg))
            {
                string roleValue = roleArg[(roleArg.IndexOf(':') + 1)..];
                if (!string.IsNullOrWhiteSpace(roleValue))
                {
                    mcpRole = roleValue;
                }
            }

            // Ensure that when MCP stdio is enabled, mcpRole is always non-null.
            // This matches the NotNullWhen(true) contract and avoids nullable warnings
            // for callers while still allowing an implicit default when no role is provided.
            mcpRole ??= "anonymous";

            return true;
        }

        /// <summary>
        /// Configures the IConfigurationBuilder for MCP stdio mode.
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="mcpRole"></param>
        public static void ConfigureMcpStdio(IConfigurationBuilder builder, string? mcpRole)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MCP:StdioMode"] = "true",
                ["MCP:Role"] = mcpRole ?? "anonymous",
                ["Runtime:Host:Authentication:Provider"] = "Simulator"
            });
        }

        /// <summary>
        /// Runs the MCP stdio host.
        /// </summary>
        /// <param name="host"> The host to run.</param>
        public static bool RunMcpStdioHost(IHost host)
        {
            host.Start();

            Mcp.Core.McpToolRegistry registry =
                host.Services.GetRequiredService<Mcp.Core.McpToolRegistry>();
            IEnumerable<Mcp.Model.IMcpTool> tools =
                host.Services.GetServices<Mcp.Model.IMcpTool>();

            foreach (Mcp.Model.IMcpTool tool in tools)
            {
                registry.RegisterTool(tool);
            }

            IHostApplicationLifetime lifetime =
                host.Services.GetRequiredService<IHostApplicationLifetime>();
            Mcp.Core.IMcpStdioServer stdio =
                host.Services.GetRequiredService<Mcp.Core.IMcpStdioServer>();

            stdio.RunAsync(lifetime.ApplicationStopping).GetAwaiter().GetResult();
            host.StopAsync().GetAwaiter().GetResult();

            return true;
        }
    }
}
