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
                .Add("hosting", container == true ? "generic_container" : "unknown")
                .Add("container", container switch { true => "enabled", false => "disabled", _ => "unknown" })
                // Test mode is not evidence of packaging or a release channel.
                .Add("distribution", "unknown")
                .Add("release_channel", "unknown")
                .Add("packaging", "unknown");
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
