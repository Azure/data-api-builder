// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Azure.DataApiBuilder.Config;
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
        /// <param name="mcpRole"> The role for MCP stdio mode. When this method returns true, the role defaults to anonymous.</param>
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
            try
            {
                // This process entry point is deliberately synchronous and runs without an
                // ASP.NET, UI, or other custom SynchronizationContext. Bridging the two async
                // operations with GetAwaiter().GetResult() therefore cannot deadlock on a
                // captured context and preserves direct exception propagation.
                // Stdio deliberately does not start the web host, so Startup.Configure does not
                // initialize runtime dependencies. Run the same serialized validation, metadata,
                // and registry sequence used by HTTP startup before opening the stdio loop.
                RuntimeInitializationHelper
                    .InitializeRuntimeDependenciesAsync(host.Services)
                    .GetAwaiter()
                    .GetResult();

                IHostApplicationLifetime lifetime =
                    host.Services.GetRequiredService<IHostApplicationLifetime>();
                Mcp.Core.IMcpStdioServer stdio =
                    host.Services.GetRequiredService<Mcp.Core.IMcpStdioServer>();

                stdio.RunAsync(lifetime.ApplicationStopping).GetAwaiter().GetResult();

                return true;
            }
            finally
            {
                host.Services
                    .GetService<FileSystemRuntimeConfigLoader>()?
                    .StopAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                host.Dispose();
            }
        }
    }
}
