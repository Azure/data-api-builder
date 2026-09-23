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
        /// Replaces the complete registry with a snapshot built for <paramref name="config"/>.
        /// The candidate is validated and materialized before it is atomically published.
        /// </summary>
        internal McpToolRegistryUpdateResult ReplaceAll(IEnumerable<IMcpTool> tools, RuntimeConfig config)
        {
            return PublishCandidate(CreateCandidate(tools, config, CancellationToken.None));
        }

        /// <summary>
        /// Builds and validates a complete replacement without publishing it.
        /// </summary>
        internal static McpToolRegistryCandidate CreateCandidate(
            IEnumerable<IMcpTool> tools,
            RuntimeConfig config,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(tools);
            ArgumentNullException.ThrowIfNull(config);
            cancellationToken.ThrowIfCancellationRequested();

            ImmutableDictionary<string, IMcpTool>.Builder toolBuilder =
                ImmutableDictionary.CreateBuilder<string, IMcpTool>(StringComparer.OrdinalIgnoreCase);
            List<Tool> advertisedMetadata = new();

            foreach (IMcpTool tool in tools)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            string discoveryJson = CreateDiscoveryJson(advertisedTools);
            return new McpToolRegistryCandidate(
                Tools: toolBuilder.ToImmutable(),
                AdvertisedToolCount: advertisedTools.Length,
                DiscoveryJson: discoveryJson,
                DiscoveryCanonicalJson: CreateDiscoveryCanonicalJson(discoveryJson));
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
                    AdvertisedToolCount: candidate.AdvertisedToolCount,
                    DiscoveryJson: candidate.DiscoveryJson,
                    DiscoveryCanonicalJson: candidate.DiscoveryCanonicalJson);

                Interlocked.Exchange(ref _snapshot, replacement);

                return new McpToolRegistryUpdateResult(
                    Version: replacement.Version,
                    DiscoveryChanged: !string.Equals(
                        current.DiscoveryCanonicalJson,
                        replacement.DiscoveryCanonicalJson,
                        StringComparison.Ordinal),
                    RegisteredToolCount: replacement.Tools.Count,
                    AdvertisedToolCount: replacement.AdvertisedToolCount);
            }
        }

        /// <summary>
        /// Gets the metadata snapshot advertised by <c>tools/list</c>.
        /// </summary>
        /// <remarks>
        /// Returns defensive deep clones so callers cannot mutate the private snapshot shared by
        /// concurrent readers. Candidate construction serializes an order-preserving discovery
        /// representation for serving and a separate canonical representation for semantic change
        /// comparison. Discovery deserializes the serving representation instead of serializing
        /// every tool again on each request, while still allocating caller-owned objects.
        /// </remarks>
        public IReadOnlyList<Tool> GetAdvertisedTools()
        {
            McpToolRegistrySnapshot snapshot = Volatile.Read(ref _snapshot);
            return JsonSerializer.Deserialize<Tool[]>(
                snapshot.DiscoveryJson,
                _discoveryJsonOptions)
                ?? throw new InvalidOperationException(
                    "Failed to clone advertised MCP tool metadata.");
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

        private static string CreateDiscoveryJson(ImmutableArray<Tool> metadata)
        {
            return JsonSerializer.Serialize(
                metadata.ToArray(),
                _discoveryJsonOptions);
        }

        private static string CreateDiscoveryCanonicalJson(string discoveryJson)
        {
            using JsonDocument serializedMetadata = JsonDocument.Parse(discoveryJson);
            using MemoryStream canonicalJson = new();
            using (Utf8JsonWriter writer = new(canonicalJson))
            {
                writer.WriteStartArray();
                foreach (JsonElement tool in serializedMetadata.RootElement.EnumerateArray())
                {
                    WriteCanonicalJson(writer, tool, CanonicalJsonContext.Tool);
                }

                writer.WriteEndArray();
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

        private static void WriteCanonicalJson(
            Utf8JsonWriter writer,
            JsonElement element,
            CanonicalJsonContext context)
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
                        WriteCanonicalJson(
                            writer,
                            property.Value,
                            GetCanonicalPropertyContext(context, property));
                    }

                    writer.WriteEndObject();
                    break;

                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    if (context != CanonicalJsonContext.SchemaStringSet ||
                        !TryWriteOrderInsensitiveJsonSchemaStringArray(writer, element))
                    {
                        CanonicalJsonContext itemContext = context == CanonicalJsonContext.SchemaArray
                            ? CanonicalJsonContext.Schema
                            : CanonicalJsonContext.Data;
                        foreach (JsonElement item in element.EnumerateArray())
                        {
                            WriteCanonicalJson(writer, item, itemContext);
                        }
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

        /// <summary>
        /// Only the tool's direct input/output schema fields introduce schema context. Within a
        /// schema, follow known subschema keywords; defaults, constants, examples, enum instances,
        /// and unknown extensions remain ordinary data at every nesting depth.
        /// </summary>
        private static CanonicalJsonContext GetCanonicalPropertyContext(
            CanonicalJsonContext context,
            JsonProperty property)
        {
            return context switch
            {
                CanonicalJsonContext.Tool when property.Name is "inputSchema" or "outputSchema" =>
                    CanonicalJsonContext.Schema,
                // Map keys are instance property/definition names, not schema keywords. Legacy
                // dependencies can also contain string arrays, which the schema context preserves.
                CanonicalJsonContext.SchemaMap => CanonicalJsonContext.Schema,
                CanonicalJsonContext.Schema => property.Name switch
                {
                    "required" or "type" or "enum" => CanonicalJsonContext.SchemaStringSet,
                    "properties" or "patternProperties" or "$defs" or "definitions" or
                        "dependentSchemas" or "dependencies" => CanonicalJsonContext.SchemaMap,
                    "allOf" or "anyOf" or "oneOf" or "prefixItems" => CanonicalJsonContext.SchemaArray,
                    // Before draft 2020-12, items also allowed an ordered array of tuple schemas.
                    "items" when property.Value.ValueKind == JsonValueKind.Array => CanonicalJsonContext.SchemaArray,
                    "items" or "additionalItems" or "additionalProperties" or "unevaluatedItems" or
                        "unevaluatedProperties" or "contains" or "propertyNames" or "not" or
                        "if" or "then" or "else" or "contentSchema" => CanonicalJsonContext.Schema,
                    _ => CanonicalJsonContext.Data
                },
                _ => CanonicalJsonContext.Data
            };
        }

        private static bool TryWriteOrderInsensitiveJsonSchemaStringArray(
            Utf8JsonWriter writer,
            JsonElement element)
        {
            // Called only for a schema's own required/type/enum keyword. Non-string enum entries
            // are instance data and must not be traversed as schemas when this returns false.
            List<string> values = new();
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                values.Add(item.GetString()!);
            }

            values.Sort(StringComparer.Ordinal);
            foreach (string value in values)
            {
                writer.WriteStringValue(value);
            }

            return true;
        }

        private enum CanonicalJsonContext
        {
            Data,
            Tool,
            Schema,
            SchemaMap,
            SchemaArray,
            SchemaStringSet
        }

        private sealed record McpToolRegistrySnapshot(
            long Version,
            ImmutableDictionary<string, IMcpTool> Tools,
            int AdvertisedToolCount,
            string DiscoveryJson,
            string DiscoveryCanonicalJson)
        {
            public static McpToolRegistrySnapshot Empty { get; } = new(
                Version: 0,
                Tools: ImmutableDictionary.Create<string, IMcpTool>(StringComparer.OrdinalIgnoreCase),
                AdvertisedToolCount: 0,
                DiscoveryJson: "[]",
                DiscoveryCanonicalJson: "[]");
        }
    }

    /// <summary>
    /// Describes the result of atomically replacing an MCP registry snapshot.
    /// </summary>
    internal readonly record struct McpToolRegistryUpdateResult(
        long Version,
        bool DiscoveryChanged,
        int RegisteredToolCount,
        int AdvertisedToolCount);

    /// <summary>
    /// A fully materialized and validated registry generation awaiting publication.
    /// </summary>
    internal sealed record McpToolRegistryCandidate(
        ImmutableDictionary<string, IMcpTool> Tools,
        int AdvertisedToolCount,
        string DiscoveryJson,
        string DiscoveryCanonicalJson);
}
