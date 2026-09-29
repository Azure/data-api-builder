// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Azure.DataApiBuilder.Service.Utilities
{
    /// <summary>
    /// Helper methods for configuring and running MCP in stdio mode.
    /// </summary>
    internal static class McpStdioHelper
    {
        /// <summary>
        /// Reported when the MCP stdio host fails, mirroring the single message the web path uses in
        /// Startup.PerformOnConfigChangeAsync. Deliberately not "startup": the catch also covers the
        /// stdio loop, so a mid-session failure reports through here too.
        /// </summary>
        private const string STDIO_HOST_FAILED_MESSAGE =
            "Unable to run the MCP stdio host. Refer to exception for error details.";

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
        /// <returns>True when the stdio loop ran to completion; false when startup or the loop failed and was
        /// reported, which Program.Main surfaces as a non-zero exit code.</returns>
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
                IMcpStdioServer stdio =
                    host.Services.GetRequiredService<IMcpStdioServer>();

                stdio.RunAsync(lifetime.ApplicationStopping).GetAwaiter().GetResult();

                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Mirrors Startup.PerformOnConfigChangeAsync: report and return false instead of letting
                // the exception escape a method whose contract is a bool, and Program.Main turns that
                // false into ExitCode -1. Cancellation is left to Program.StartEngine's own handler.
                // ILogger reaches nobody this early -- stdio keeps only McpLoggerProvider, which stays
                // disabled until the client sends logging/setLevel, impossible before the JSON-RPC loop
                // runs -- so stderr is the only open channel. At the --mcp-stdio default of LogLevel.None
                // Program has already pointed stderr at TextWriter.Null, so write the stream directly in
                // that case rather than installing a writer that would outlive this call. stdout is left
                // untouched for JSON-RPC.
                string report = $"{STDIO_HOST_FAILED_MESSAGE} {ex}";

                if (ReferenceEquals(Console.Error, TextWriter.Null))
                {
                    using StreamWriter standardError = new(
                        Console.OpenStandardError(),
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    standardError.WriteLine(report);
                }
                else
                {
                    Console.Error.WriteLine(report);
                }

                return false;
            }
            finally
            {
                FileSystemRuntimeConfigLoader? configLoader =
                    host.Services.GetService<FileSystemRuntimeConfigLoader>();
                if (configLoader is not null)
                {
                    TimeSpan shutdownTimeout = host.Services
                        .GetService<IOptions<HostOptions>>()?
                        .Value.ShutdownTimeout ?? new HostOptions().ShutdownTimeout;
                    using CancellationTokenSource shutdownCancellation = new(shutdownTimeout);
                    try
                    {
                        configLoader
                            .StopAsync(shutdownCancellation.Token)
                            .GetAwaiter()
                            .GetResult();
                    }
                    catch (OperationCanceledException)
                        when (shutdownCancellation.IsCancellationRequested)
                    {
                        // Match Generic Host shutdown semantics: cancellation bounds the drain.
                    }
                }

                host.Dispose();
            }
        }
    }
}
