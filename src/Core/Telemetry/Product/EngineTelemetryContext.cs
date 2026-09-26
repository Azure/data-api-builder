// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using Azure.DataApiBuilder.Product;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>Fixed, categorical context for one engine run. Never emits raw environment values.</summary>
    internal static class EngineTelemetryContext
    {
        internal static ImmutableDictionary<string, string> Create(string executionMode, Func<string, string?> readEnvironmentVariable)
        {
            string operatingSystem = GetOperatingSystem();
            bool? container = bool.TryParse(readEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), out bool value) ? value : null;
            string launcher = Assembly.GetEntryAssembly()?.GetName().Name switch
            {
                "Microsoft.DataApiBuilder" => "cli",
                "Azure.DataApiBuilder.Service" => "standalone",
                _ => "unknown"
            };

            return ImmutableDictionary<string, string>.Empty
                .Add("dab_version", ProductInfo.GetProductVersion())
                .Add("os_family", operatingSystem)
                .Add("os_version", operatingSystem == "unknown" ? "unknown" : $"{Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}")
                .Add("architecture", GetArchitecture())
                .Add("dotnet_version", $"{Environment.Version.Major}.{Environment.Version.Minor}.{Environment.Version.Build}")
                .Add("execution_mode", executionMode is "web" or "mcp_stdio" or "embedded" ? executionMode : "unknown")
                .Add("launcher", launcher)
                .Add("hosting", GetHosting(container, readEnvironmentVariable))
                .Add("container", container switch { true => "enabled", false => "disabled", _ => "unknown" })
                // Test mode is not evidence of packaging or a release channel.
                .Add("distribution", "unknown")
                .Add("release_channel", "unknown")
                .Add("packaging", "unknown");
        }

        private static string GetHosting(bool? container, Func<string, string?> readEnvironmentVariable)
        {
            // An explicit negative container flag conflicts with an orchestrator inference.
            // Keep it as a separate observation and do not guess a hosting platform.
            if (container == false)
            {
                return "unknown";
            }

            // Fixed allowlist; reduce values immediately to presence. Do not retain names,
            // addresses, ports, credentials, or infer a region from any of these values.
            bool app = Present("CONTAINER_APP_NAME");
            bool revision = Present("CONTAINER_APP_REVISION");
            bool job = Present("CONTAINER_APP_JOB_NAME");
            bool execution = Present("CONTAINER_APP_JOB_EXECUTION_NAME");
            bool kubernetesHost = Present("KUBERNETES_SERVICE_HOST");
            bool kubernetesPort = Present("KUBERNETES_SERVICE_PORT_HTTPS");
            if ((app && revision) || (job && execution))
            {
                // The more specific managed-platform evidence wins over Kubernetes signals.
                return "azure_container_apps";
            }

            // Partial ACA evidence is insufficient to name that platform, but also prevents
            // falling through to a less-specific orchestrator classification.
            if (!(app || revision || job || execution) && kubernetesHost && kubernetesPort)
            {
                return "kubernetes";
            }

            return container == true ? "generic_container" : "unknown";

            bool Present(string name) => !string.IsNullOrWhiteSpace(readEnvironmentVariable(name));
        }

        private static string GetOperatingSystem()
        {
            if (OperatingSystem.IsWindows())
            {
                return "windows";
            }

            if (OperatingSystem.IsLinux())
            {
                return "linux";
            }

            return OperatingSystem.IsMacOS() ? "macos" : "unknown";
        }

        private static string GetArchitecture() => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => "unknown"
        };
    }
}
