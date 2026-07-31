// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Net;
using System.Text;
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

            Tool metadata = CloneMetadata(tool.GetToolMetadata());
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
                string discoveryCanonicalJson = CreateDiscoveryCanonicalJson(advertisedTools);

                Interlocked.Exchange(
                    ref _snapshot,
                    new McpToolRegistrySnapshot(
                        Version: current.Version + 1,
                        Tools: tools,
                        AdvertisedTools: advertisedTools,
                        DiscoveryCanonicalJson: discoveryCanonicalJson));
            }
        }

        /// <summary>
        /// Replaces the complete registry with a snapshot built for <paramref name="config"/>.
        /// The candidate is validated and materialized before it is atomically published.
        /// </summary>
        public McpToolRegistryUpdateResult ReplaceAll(IEnumerable<IMcpTool> tools, RuntimeConfig config)
        {
            return PublishCandidate(CreateCandidate(tools, config));
        }

        /// <summary>
        /// Builds and validates a complete replacement without publishing it.
        /// </summary>
        internal static McpToolRegistryCandidate CreateCandidate(
            IEnumerable<IMcpTool> tools,
            RuntimeConfig config)
        {
            ArgumentNullException.ThrowIfNull(tools);
            ArgumentNullException.ThrowIfNull(config);

            ImmutableDictionary<string, IMcpTool>.Builder toolBuilder =
                ImmutableDictionary.CreateBuilder<string, IMcpTool>(StringComparer.OrdinalIgnoreCase);
            List<Tool> advertisedMetadata = new();

            foreach (IMcpTool tool in tools)
            {
                ArgumentNullException.ThrowIfNull(tool);

                Tool metadata = CloneMetadata(tool.GetToolMetadata());
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
            return new McpToolRegistryCandidate(
                Tools: toolBuilder.ToImmutable(),
                AdvertisedTools: advertisedTools,
                DiscoveryCanonicalJson: CreateDiscoveryCanonicalJson(advertisedTools));
        }

        /// <summary>
        /// Atomically publishes a previously built and validated candidate.
        /// </summary>
        internal McpToolRegistryUpdateResult PublishCandidate(McpToolRegistryCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            lock (_writerLock)
            {
                McpToolRegistrySnapshot current = _snapshot;
                McpToolRegistrySnapshot replacement = new(
                    Version: current.Version + 1,
                    Tools: candidate.Tools,
                    AdvertisedTools: candidate.AdvertisedTools,
                    DiscoveryCanonicalJson: candidate.DiscoveryCanonicalJson);

                Interlocked.Exchange(ref _snapshot, replacement);

                return new McpToolRegistryUpdateResult(
                    Version: replacement.Version,
                    DiscoveryChanged: !string.Equals(
                        current.DiscoveryCanonicalJson,
                        replacement.DiscoveryCanonicalJson,
                        StringComparison.Ordinal),
                    RegisteredToolCount: replacement.Tools.Count,
                    AdvertisedToolCount: replacement.AdvertisedTools.Length);
            }
        }

        /// <summary>
        /// Gets the metadata snapshot advertised by <c>tools/list</c>.
        /// </summary>
        /// <remarks>
        /// Returns defensive deep clones so callers cannot mutate the private snapshot shared by
        /// concurrent readers. The JSON round trip is intentional and occurs only for discovery
        /// requests, not for tool lookup or execution.
        /// </remarks>
        public IReadOnlyList<Tool> GetAdvertisedTools()
        {
            McpToolRegistrySnapshot snapshot = Volatile.Read(ref _snapshot);
            return snapshot.AdvertisedTools
                .Select(CloneMetadata)
                .ToArray();
        }

        /// <summary>
        /// Gets metadata for all registered tools that are enabled in the given runtime configuration.
        /// Retained for compatibility; MCP handlers should use <see cref="GetAdvertisedTools"/> so
        /// lookup and discovery come from the same registry generation.
        /// </summary>
        [Obsolete(
            "GetEnabledTools combines a registry snapshot with caller-supplied configuration. " +
            "Use GetAdvertisedTools so discovery comes from one published generation.")]
        public IEnumerable<Tool> GetEnabledTools(RuntimeConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            McpToolRegistrySnapshot snapshot = Volatile.Read(ref _snapshot);
            return snapshot.Tools.Values
                .Where(t => t.IsEnabled(config))
                .Select(t => CloneMetadata(t.GetToolMetadata()));
        }

        /// <summary>
        /// Tries to get a tool by name
        /// </summary>
        public bool TryGetTool(string toolName, out IMcpTool? tool)
        {
            return Volatile.Read(ref _snapshot).Tools.TryGetValue(toolName, out tool);
        }

        private static string ValidateToolName(Tool metadata)
        {
            string toolName = metadata.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw new DataApiBuilderException(
                    message: "MCP tool name cannot be null, empty, or whitespace.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            if (!string.Equals(toolName, toolName.Trim(), StringComparison.Ordinal))
            {
                throw new DataApiBuilderException(
                    message: "MCP tool name cannot contain leading or trailing whitespace.",
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

        private static string CreateDiscoveryCanonicalJson(ImmutableArray<Tool> metadata)
        {
            JsonElement serializedMetadata = JsonSerializer.SerializeToElement(
                metadata.ToArray(),
                _discoveryJsonOptions);
            using MemoryStream canonicalJson = new();
            using (Utf8JsonWriter writer = new(canonicalJson))
            {
                WriteCanonicalJson(writer, serializedMetadata);
            }

            return Encoding.UTF8.GetString(canonicalJson.ToArray());
        }

        private static Tool CloneMetadata(Tool metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);

            byte[] serializedMetadata = JsonSerializer.SerializeToUtf8Bytes(
                metadata,
                _discoveryJsonOptions);
            return JsonSerializer.Deserialize<Tool>(serializedMetadata, _discoveryJsonOptions)
                ?? throw new InvalidOperationException("Failed to clone MCP tool metadata.");
        }

        private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (JsonProperty property in element
                        .EnumerateObject()
                        .OrderBy(property => property.Name, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(property.Name);
                        WriteCanonicalJson(writer, property.Value);
                    }

                    writer.WriteEndObject();
                    break;

                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        WriteCanonicalJson(writer, item);
                    }

                    writer.WriteEndArray();
                    break;

                case JsonValueKind.String:
                    writer.WriteStringValue(element.GetString());
                    break;

                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    element.WriteTo(writer);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported JSON value kind '{element.ValueKind}' in MCP tool metadata.");
            }
        }

        private sealed record McpToolRegistrySnapshot(
            long Version,
            ImmutableDictionary<string, IMcpTool> Tools,
            ImmutableArray<Tool> AdvertisedTools,
            string DiscoveryCanonicalJson)
        {
            public static McpToolRegistrySnapshot Empty { get; } = new(
                Version: 0,
                Tools: ImmutableDictionary.Create<string, IMcpTool>(StringComparer.OrdinalIgnoreCase),
                AdvertisedTools: ImmutableArray<Tool>.Empty,
                DiscoveryCanonicalJson: "[]");
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

    /// <summary>
    /// A fully materialized and validated registry generation awaiting publication.
    /// </summary>
    internal sealed record McpToolRegistryCandidate(
        ImmutableDictionary<string, IMcpTool> Tools,
        ImmutableArray<Tool> AdvertisedTools,
        string DiscoveryCanonicalJson);
}
