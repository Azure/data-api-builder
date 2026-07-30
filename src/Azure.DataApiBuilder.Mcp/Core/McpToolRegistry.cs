// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Service.Exceptions;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Registry for managing MCP tools
    /// </summary>
    public class McpToolRegistry
    {
        private static readonly JsonSerializerOptions _discoveryJsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly object _writerLock = new();
        private McpToolRegistrySnapshot _snapshot = McpToolRegistrySnapshot.Empty;

        /// <summary>
        /// Registers a tool in the registry using copy-on-write publication.
        /// This compatibility API is retained for callers that incrementally construct a registry;
        /// production initialization and hot-reload use <see cref="ReplaceAll"/>.
        /// </summary>
        /// <exception cref="DataApiBuilderException">Thrown when tool name is invalid or duplicate</exception>
        public void RegisterTool(IMcpTool tool)
        {
            ArgumentNullException.ThrowIfNull(tool);

            Tool metadata = tool.GetToolMetadata();
            string toolName = ValidateToolName(metadata);

            lock (_writerLock)
            {
                McpToolRegistrySnapshot current = _snapshot;

                if (current.Tools.TryGetValue(toolName, out IMcpTool? existingTool))
                {
                    if (ReferenceEquals(existingTool, tool))
                    {
                        return;
                    }

                    throw CreateDuplicateToolException(toolName, existingTool, tool);
                }

                ImmutableDictionary<string, IMcpTool> tools = current.Tools.Add(toolName, tool);
                ImmutableArray<Tool> advertisedTools = SortMetadata(current.AdvertisedTools.Add(metadata));
                string fingerprint = CreateDiscoveryFingerprint(advertisedTools);

                Interlocked.Exchange(
                    ref _snapshot,
                    new McpToolRegistrySnapshot(
                        Version: current.Version + 1,
                        Tools: tools,
                        AdvertisedTools: advertisedTools,
                        DiscoveryFingerprint: fingerprint));
            }
        }

        /// <summary>
        /// Replaces the complete registry with a snapshot built for <paramref name="config"/>.
        /// The candidate is validated and materialized before it is atomically published.
        /// </summary>
        public McpToolRegistryUpdateResult ReplaceAll(IEnumerable<IMcpTool> tools, RuntimeConfig config)
        {
            ArgumentNullException.ThrowIfNull(tools);
            ArgumentNullException.ThrowIfNull(config);

            lock (_writerLock)
            {
                ImmutableDictionary<string, IMcpTool>.Builder toolBuilder =
                    ImmutableDictionary.CreateBuilder<string, IMcpTool>(StringComparer.OrdinalIgnoreCase);
                List<Tool> advertisedMetadata = new();

                foreach (IMcpTool tool in tools)
                {
                    ArgumentNullException.ThrowIfNull(tool);

                    Tool metadata = tool.GetToolMetadata();
                    string toolName = ValidateToolName(metadata);

                    if (toolBuilder.TryGetValue(toolName, out IMcpTool? existingTool))
                    {
                        if (ReferenceEquals(existingTool, tool))
                        {
                            continue;
                        }

                        throw CreateDuplicateToolException(toolName, existingTool, tool);
                    }

                    toolBuilder.Add(toolName, tool);
                    if (tool.IsEnabled(config))
                    {
                        advertisedMetadata.Add(metadata);
                    }
                }

                ImmutableArray<Tool> advertisedTools = SortMetadata(advertisedMetadata);
                string fingerprint = CreateDiscoveryFingerprint(advertisedTools);
                McpToolRegistrySnapshot current = _snapshot;
                McpToolRegistrySnapshot replacement = new(
                    Version: current.Version + 1,
                    Tools: toolBuilder.ToImmutable(),
                    AdvertisedTools: advertisedTools,
                    DiscoveryFingerprint: fingerprint);

                Interlocked.Exchange(ref _snapshot, replacement);

                return new McpToolRegistryUpdateResult(
                    Version: replacement.Version,
                    DiscoveryChanged: !string.Equals(
                        current.DiscoveryFingerprint,
                        replacement.DiscoveryFingerprint,
                        StringComparison.Ordinal),
                    RegisteredToolCount: replacement.Tools.Count,
                    AdvertisedToolCount: replacement.AdvertisedTools.Length);
            }
        }

        /// <summary>
        /// Gets the metadata snapshot advertised by <c>tools/list</c>.
        /// </summary>
        public IReadOnlyList<Tool> GetAdvertisedTools()
        {
            return Volatile.Read(ref _snapshot).AdvertisedTools;
        }

        /// <summary>
        /// Gets metadata for all registered tools that are enabled in the given runtime configuration.
        /// Retained for compatibility; MCP handlers should use <see cref="GetAdvertisedTools"/> so
        /// lookup and discovery come from the same registry generation.
        /// </summary>
        public IEnumerable<Tool> GetEnabledTools(RuntimeConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            McpToolRegistrySnapshot snapshot = Volatile.Read(ref _snapshot);
            return snapshot.Tools.Values
                .Where(t => t.IsEnabled(config))
                .Select(t => t.GetToolMetadata());
        }

        /// <summary>
        /// Tries to get a tool by name
        /// </summary>
        public bool TryGetTool(string toolName, out IMcpTool? tool)
        {
            return Volatile.Read(ref _snapshot).Tools.TryGetValue(toolName, out tool);
        }

        /// <summary>
        /// Initializes and registers all MCP tools, enriching custom tools with DB metadata schemas.
        /// Shared by both HTTP hosted-service and stdio startup paths.
        /// </summary>
        public static void InitializeAndRegisterTools(
            IEnumerable<IMcpTool> tools,
            McpToolRegistry registry,
            IServiceProvider serviceProvider)
        {
            foreach (IMcpTool tool in tools)
            {
                if (tool is DynamicCustomTool customTool)
                {
                    customTool.InitializeMetadata(serviceProvider);
                }

                registry.RegisterTool(tool);
            }
        }

        private static string ValidateToolName(Tool metadata)
        {
            string toolName = metadata.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw new DataApiBuilderException(
                    message: "MCP tool name cannot be null, empty, or whitespace.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            return toolName;
        }

        private static DataApiBuilderException CreateDuplicateToolException(
            string toolName,
            IMcpTool existingTool,
            IMcpTool newTool)
        {
            string existingToolType = existingTool.ToolType == ToolType.BuiltIn ? "built-in" : "custom";
            string newToolType = newTool.ToolType == ToolType.BuiltIn ? "built-in" : "custom";

            return new DataApiBuilderException(
                message: $"Duplicate MCP tool name '{toolName}' detected. " +
                        $"A {existingToolType} tool with this name is already registered. " +
                        $"Cannot register {newToolType} tool with the same name. " +
                        $"Tool names must be unique across all tool types.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        private static ImmutableArray<Tool> SortMetadata(IEnumerable<Tool> metadata)
        {
            return metadata
                .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(tool => tool.Name, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        private static string CreateDiscoveryFingerprint(ImmutableArray<Tool> metadata)
        {
            return JsonSerializer.Serialize(metadata.ToArray(), _discoveryJsonOptions);
        }

        private sealed record McpToolRegistrySnapshot(
            long Version,
            ImmutableDictionary<string, IMcpTool> Tools,
            ImmutableArray<Tool> AdvertisedTools,
            string DiscoveryFingerprint)
        {
            public static McpToolRegistrySnapshot Empty { get; } = new(
                Version: 0,
                Tools: ImmutableDictionary.Create<string, IMcpTool>(StringComparer.OrdinalIgnoreCase),
                AdvertisedTools: ImmutableArray<Tool>.Empty,
                DiscoveryFingerprint: "[]");
        }
    }

    /// <summary>
    /// Describes the result of atomically replacing an MCP registry snapshot.
    /// </summary>
    public readonly record struct McpToolRegistryUpdateResult(
        long Version,
        bool DiscoveryChanged,
        int RegisteredToolCount,
        int AdvertisedToolCount);
}
