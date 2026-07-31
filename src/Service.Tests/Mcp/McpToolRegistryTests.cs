// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Tests for McpToolRegistry to ensure tool name uniqueness validation.
    /// </summary>
    [TestClass]
    public class McpToolRegistryTests
    {
        /// <summary>
        /// Test that TryGetTool returns false for non-existent tool.
        /// </summary>
        [TestMethod]
        public void TryGetTool_WithNonExistentName_ReturnsFalse()
        {
            // Arrange
            McpToolRegistry registry = new();

            // Act
            bool found = registry.TryGetTool("non_existent_tool", out IMcpTool? tool);

            // Assert
            Assert.IsFalse(found);
            Assert.IsNull(tool);
        }

        /// <summary>
        /// Test edge case: empty tool name should throw exception.
        /// </summary>
        [TestMethod]
        public void ReplaceAll_WithEmptyToolName_ThrowsException()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool tool = new MockMcpTool("", ToolType.BuiltIn);

            // Assert - Empty tool names should be rejected
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => registry.ReplaceAll(new[] { tool }, CreateRuntimeConfig())
            );

            Assert.IsTrue(exception.Message.Contains("cannot be null, empty, or whitespace"));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, exception.SubStatusCode);
        }

        /// <summary>
        /// Test that leading/trailing whitespace is rejected rather than producing a lookup key
        /// that differs from the advertised tool name.
        /// </summary>
        [TestMethod]
        public void ReplaceAll_WithLeadingTrailingWhitespace_ThrowsException()
        {
            McpToolRegistry registry = new();
            IMcpTool tool = new MockMcpTool(" my_tool ", ToolType.Custom);

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => registry.ReplaceAll(new[] { tool }, CreateRuntimeConfig()));

            StringAssert.Contains(exception.Message, "leading or trailing whitespace");
            Assert.IsFalse(registry.TryGetTool("my_tool", out _));
        }

        /// <summary>
        /// Replacing the registry publishes a complete, deterministically ordered snapshot and
        /// removes tools that belonged only to the previous generation.
        /// </summary>
        [TestMethod]
        public void ReplaceAll_PublishesCompleteOrderedSnapshot()
        {
            McpToolRegistry registry = new();
            RuntimeConfig config = CreateRuntimeConfig();
            registry.ReplaceAll(
                new[] { new MockMcpTool("old_tool", ToolType.Custom) },
                config);

            McpToolRegistryUpdateResult result = registry.ReplaceAll(
                new IMcpTool[]
                {
                    new MockMcpTool("z_tool", ToolType.Custom),
                    new MockMcpTool("A_tool", ToolType.BuiltIn)
                },
                config);

            Assert.IsFalse(registry.TryGetTool("old_tool", out _));
            Assert.IsTrue(registry.TryGetTool("a_TOOL", out _));
            Assert.IsTrue(registry.TryGetTool("z_tool", out _));
            CollectionAssert.AreEqual(
                new[] { "A_tool", "z_tool" },
                registry.GetAdvertisedTools().Select(tool => tool.Name).ToArray());
            Assert.AreEqual(2, result.Version);
            Assert.IsTrue(result.DiscoveryChanged);
            Assert.AreEqual(2, result.RegisteredToolCount);
            Assert.AreEqual(2, result.AdvertisedToolCount);
        }

        /// <summary>
        /// Every name returned by discovery resolves against the exact same registry generation.
        /// </summary>
        [TestMethod]
        public void ReplaceAll_EveryAdvertisedNameIsCallable()
        {
            McpToolRegistry registry = new();
            registry.ReplaceAll(
                new IMcpTool[]
                {
                    new MockMcpTool("A_tool", ToolType.BuiltIn),
                    new MockMcpTool("z_tool", ToolType.Custom)
                },
                CreateRuntimeConfig());

            foreach (Tool advertisedTool in registry.GetAdvertisedTools())
            {
                Assert.IsTrue(
                    registry.TryGetTool(advertisedTool.Name, out IMcpTool? callableTool),
                    $"Advertised MCP tool '{advertisedTool.Name}' must be callable by that exact name.");
                Assert.IsNotNull(callableTool);
            }
        }

        /// <summary>
        /// A candidate containing a duplicate name is rejected before publication, leaving the
        /// complete previous snapshot active.
        /// </summary>
        [TestMethod]
        public void ReplaceAll_WithDuplicateName_PreservesPreviousSnapshot()
        {
            McpToolRegistry registry = new();
            RuntimeConfig config = CreateRuntimeConfig();
            IMcpTool previousTool = new MockMcpTool("previous_tool", ToolType.BuiltIn);
            registry.ReplaceAll(new[] { previousTool }, config);

            Assert.ThrowsException<DataApiBuilderException>(() => registry.ReplaceAll(
                new IMcpTool[]
                {
                    new MockMcpTool("duplicate", ToolType.BuiltIn),
                    new MockMcpTool("DUPLICATE", ToolType.Custom)
                },
                config));

            Assert.IsTrue(registry.TryGetTool("previous_tool", out IMcpTool? actualTool));
            Assert.AreSame(previousTool, actualTool);
            Assert.IsFalse(registry.TryGetTool("duplicate", out _));
            CollectionAssert.AreEqual(
                new[] { "previous_tool" },
                registry.GetAdvertisedTools().Select(tool => tool.Name).ToArray());
        }

        /// <summary>
        /// Replacing tool instances with semantically identical discovery metadata advances the
        /// registry generation without reporting a client-visible discovery change.
        /// </summary>
        [TestMethod]
        public void ReplaceAll_WithEquivalentMetadata_DoesNotReportDiscoveryChange()
        {
            McpToolRegistry registry = new();
            RuntimeConfig config = CreateRuntimeConfig();
            registry.ReplaceAll(
                new[] { new MockMcpTool("same_tool", ToolType.Custom, description: "Same description") },
                config);

            McpToolRegistryUpdateResult result = registry.ReplaceAll(
                new[] { new MockMcpTool("same_tool", ToolType.Custom, description: "Same description") },
                config);

            Assert.AreEqual(2, result.Version);
            Assert.IsFalse(result.DiscoveryChanged);
        }

        /// <summary>
        /// Object property order is not semantically meaningful and must not trigger discovery
        /// invalidation when equivalent metadata is rebuilt in a different insertion order.
        /// </summary>
        [TestMethod]
        public void ReplaceAll_WithEquivalentSchemaPropertyOrder_DoesNotReportDiscoveryChange()
        {
            const string SCHEMA_AB =
                "{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"},\"b\":{\"type\":\"integer\"}}}";
            const string SCHEMA_BA =
                "{\"properties\":{\"b\":{\"type\":\"integer\"},\"a\":{\"type\":\"string\"}},\"type\":\"object\"}";
            McpToolRegistry registry = new();
            RuntimeConfig config = CreateRuntimeConfig();
            registry.ReplaceAll(
                new[] { new MockMcpTool("same_tool", ToolType.Custom, inputSchemaJson: SCHEMA_AB) },
                config);

            McpToolRegistryUpdateResult result = registry.ReplaceAll(
                new[] { new MockMcpTool("same_tool", ToolType.Custom, inputSchemaJson: SCHEMA_BA) },
                config);

            Assert.IsFalse(result.DiscoveryChanged);
        }

        /// <summary>
        /// Canonical property sorting is used only for change detection. The discovery payload
        /// preserves schema-property insertion order for clients that render parameters in wire
        /// order even though JSON Schema does not assign that order semantic meaning.
        /// </summary>
        [TestMethod]
        public void GetAdvertisedTools_PreservesInputSchemaPropertyOrder()
        {
            const string SCHEMA =
                "{\"type\":\"object\",\"properties\":{" +
                "\"second\":{\"type\":\"string\"}," +
                "\"first\":{\"type\":\"integer\"}}}";
            McpToolRegistry registry = new();
            registry.ReplaceAll(
                new[] { new MockMcpTool("ordered_tool", ToolType.Custom, inputSchemaJson: SCHEMA) },
                CreateRuntimeConfig());

            string[] propertyNames = registry.GetAdvertisedTools()
                .Single()
                .InputSchema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray();

            CollectionAssert.AreEqual(new[] { "second", "first" }, propertyNames);
        }

        /// <summary>
        /// A real input-schema change remains client-visible after canonicalization.
        /// </summary>
        [TestMethod]
        public void ReplaceAll_WithChangedInputSchema_ReportsDiscoveryChange()
        {
            McpToolRegistry registry = new();
            RuntimeConfig config = CreateRuntimeConfig();
            registry.ReplaceAll(
                new[]
                {
                    new MockMcpTool(
                        "same_tool",
                        ToolType.Custom,
                        inputSchemaJson: "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}}}")
                },
                config);

            McpToolRegistryUpdateResult result = registry.ReplaceAll(
                new[]
                {
                    new MockMcpTool(
                        "same_tool",
                        ToolType.Custom,
                        inputSchemaJson: "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"integer\"}}}")
                },
                config);

            Assert.IsTrue(result.DiscoveryChanged);
        }

        /// <summary>
        /// Published metadata is isolated both from the tool-owned source object and from callers
        /// mutating a value returned by the public snapshot accessor.
        /// </summary>
        [TestMethod]
        public void ReplaceAll_DefensivelyClonesPublishedMetadata()
        {
            Tool retainedMetadata = new()
            {
                Name = "isolated_tool",
                Description = "Original description",
                InputSchema = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\"}")
            };
            McpToolRegistry registry = new();
            registry.ReplaceAll(
                new[] { new RetainedMetadataMcpTool(retainedMetadata) },
                CreateRuntimeConfig());

            retainedMetadata.Description = "Mutated by tool";
            Tool returnedMetadata = registry.GetAdvertisedTools().Single();
            Assert.AreEqual("Original description", returnedMetadata.Description);

            returnedMetadata.Description = "Mutated by caller";
            Assert.AreEqual(
                "Original description",
                registry.GetAdvertisedTools().Single().Description);
        }

        /// <summary>
        /// A metadata-only change is reported so connected clients can refresh their cached list.
        /// </summary>
        [TestMethod]
        public void ReplaceAll_WithChangedDescription_ReportsDiscoveryChange()
        {
            McpToolRegistry registry = new();
            RuntimeConfig config = CreateRuntimeConfig();
            registry.ReplaceAll(
                new[] { new MockMcpTool("same_tool", ToolType.Custom, description: "Old description") },
                config);

            McpToolRegistryUpdateResult result = registry.ReplaceAll(
                new[] { new MockMcpTool("same_tool", ToolType.Custom, description: "New description") },
                config);

            Assert.IsTrue(result.DiscoveryChanged);
            Assert.AreEqual("New description", registry.GetAdvertisedTools().Single().Description);
        }

        /// <summary>
        /// Advertised metadata and callable lookup state are built from one candidate generation.
        /// Disabled built-ins remain callable so execution can return the existing structured
        /// tool-disabled response, but they are absent from discovery.
        /// </summary>
        [TestMethod]
        public void ReplaceAll_CapturesVisibilityFromCandidateConfig()
        {
            McpToolRegistry registry = new();
            IMcpTool configAwareTool = new MockMcpTool(
                "create_record",
                ToolType.BuiltIn,
                isEnabledFunc: config => config.McpDmlTools?.CreateRecord == true);

            RuntimeConfig disabledConfig = CreateRuntimeConfig(new DmlToolsConfig(createRecord: false));
            registry.ReplaceAll(new[] { configAwareTool }, disabledConfig);

            Assert.AreEqual(0, registry.GetAdvertisedTools().Count);
            Assert.IsTrue(registry.TryGetTool("create_record", out _));

            RuntimeConfig enabledConfig = CreateRuntimeConfig(new DmlToolsConfig(createRecord: true));
            registry.ReplaceAll(new[] { configAwareTool }, enabledConfig);

            Assert.AreEqual(1, registry.GetAdvertisedTools().Count);
        }

        /// <summary>
        /// Concurrent readers see only a complete old or complete new advertised snapshot while
        /// registry generations are repeatedly replaced.
        /// </summary>
        [TestMethod]
        public void ReplaceAll_WithConcurrentReaders_NeverExposesPartialSnapshot()
        {
            McpToolRegistry registry = new();
            RuntimeConfig config = CreateRuntimeConfig();
            IMcpTool[] generationA =
            {
                new MockMcpTool("a_one", ToolType.BuiltIn),
                new MockMcpTool("a_two", ToolType.Custom)
            };
            IMcpTool[] generationB =
            {
                new MockMcpTool("b_one", ToolType.BuiltIn),
                new MockMcpTool("b_two", ToolType.Custom)
            };
            registry.ReplaceAll(generationA, config);

            ConcurrentQueue<string> invalidSnapshots = new();
            Task writer = Task.Run(() =>
            {
                for (int i = 0; i < 500; i++)
                {
                    registry.ReplaceAll(i % 2 == 0 ? generationB : generationA, config);
                }
            });

            Task[] readers = Enumerable.Range(0, 4)
                .Select(_ => Task.Run(() =>
                {
                    for (int i = 0; i < 2_000; i++)
                    {
                        string[] names = registry.GetAdvertisedTools()
                            .Select(tool => tool.Name)
                            .ToArray();
                        bool isGenerationA = names.SequenceEqual(new[] { "a_one", "a_two" });
                        bool isGenerationB = names.SequenceEqual(new[] { "b_one", "b_two" });
                        if (!isGenerationA && !isGenerationB)
                        {
                            invalidSnapshots.Enqueue(string.Join(",", names));
                        }
                    }
                }))
                .ToArray();

            Task.WaitAll(readers.Append(writer).ToArray());

            Assert.AreEqual(
                0,
                invalidSnapshots.Count,
                $"Observed partial snapshots: {string.Join(" | ", invalidSnapshots.Take(5))}");
        }

        /// <summary>
        /// Validates IsEnabled for each real built-in tool matches the DmlToolsConfig flag value.
        /// </summary>
        [DataTestMethod]
        [DataRow(true, DisplayName = "All DML tools enabled")]
        [DataRow(false, DisplayName = "All DML tools disabled")]
        public void BuiltInTools_IsEnabled_MatchesDmlToolsConfigFlag(bool allEnabled)
        {
            // Arrange
            DmlToolsConfig dmlConfig = DmlToolsConfig.FromBoolean(allEnabled);
            RuntimeConfig config = CreateRuntimeConfig(dmlConfig);

            IMcpTool[] builtInTools = new IMcpTool[]
            {
                new Azure.DataApiBuilder.Mcp.BuiltInTools.CreateRecordTool(),
                new Azure.DataApiBuilder.Mcp.BuiltInTools.DeleteRecordTool(),
                new Azure.DataApiBuilder.Mcp.BuiltInTools.ReadRecordsTool(),
                new Azure.DataApiBuilder.Mcp.BuiltInTools.UpdateRecordTool(),
                new Azure.DataApiBuilder.Mcp.BuiltInTools.DescribeEntitiesTool(),
                new Azure.DataApiBuilder.Mcp.BuiltInTools.AggregateRecordsTool(),
                new Azure.DataApiBuilder.Mcp.BuiltInTools.ExecuteEntityTool()
            };

            // Act & Assert
            foreach (IMcpTool tool in builtInTools)
            {
                Assert.AreEqual(allEnabled, tool.IsEnabled(config),
                    $"{tool.GetType().Name}.IsEnabled should be {allEnabled}");
            }
        }

        /// <summary>
        /// Validates that individual DML tool flags are respected (e.g., only create-record disabled).
        /// </summary>
        [TestMethod]
        public void BuiltInTools_IsEnabled_RespectsIndividualFlags()
        {
            // Arrange - only create-record and delete-record disabled
            DmlToolsConfig selectiveConfig = new(
                createRecord: false,
                deleteRecord: false);
            RuntimeConfig config = CreateRuntimeConfig(selectiveConfig);

            // Act & Assert - disabled tools
            Assert.IsFalse(new Azure.DataApiBuilder.Mcp.BuiltInTools.CreateRecordTool().IsEnabled(config));
            Assert.IsFalse(new Azure.DataApiBuilder.Mcp.BuiltInTools.DeleteRecordTool().IsEnabled(config));

            // Act & Assert - remaining tools should be enabled (default = true)
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.ReadRecordsTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.UpdateRecordTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.DescribeEntitiesTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.AggregateRecordsTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.ExecuteEntityTool().IsEnabled(config));
        }

        /// <summary>
        /// Validates that all built-in tools default to enabled when runtime.mcp is not configured
        /// (McpDmlTools is null because Runtime.Mcp is null).
        /// </summary>
        [TestMethod]
        public void BuiltInTools_IsEnabled_DefaultsToTrueWhenMcpNotConfigured()
        {
            // Arrange - config with no Mcp section at all → McpDmlTools returns null
            RuntimeConfig config = new(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: null,
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );

            // Verify precondition: McpDmlTools is null
            Assert.IsNull(config.McpDmlTools);

            // Act & Assert - all built-in tools should default to enabled
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.CreateRecordTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.DeleteRecordTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.ReadRecordsTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.UpdateRecordTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.DescribeEntitiesTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.AggregateRecordsTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.ExecuteEntityTool().IsEnabled(config));
        }

        #region Private helpers

        /// <summary>
        /// Mock implementation of IMcpTool for testing purposes.
        /// </summary>
        private class MockMcpTool : IMcpTool
        {
            private readonly string _toolName;
            private readonly Func<RuntimeConfig, bool>? _isEnabledFunc;
            private readonly string _description;
            private readonly string _inputSchemaJson;

            public MockMcpTool(
                string toolName,
                ToolType toolType,
                Func<RuntimeConfig, bool>? isEnabledFunc = null,
                string? description = null,
                string? inputSchemaJson = null)
            {
                _toolName = toolName;
                ToolType = toolType;
                _isEnabledFunc = isEnabledFunc;
                _description = description ?? $"Mock {toolType} tool";
                _inputSchemaJson = inputSchemaJson ?? "{\"type\":\"object\"}";
            }

            public ToolType ToolType { get; }

            public bool IsEnabled(RuntimeConfig config)
            {
                return _isEnabledFunc?.Invoke(config) ?? true;
            }

            public Tool GetToolMetadata()
            {
                using JsonDocument doc = JsonDocument.Parse(_inputSchemaJson);
                return new Tool
                {
                    Name = _toolName,
                    Description = _description,
                    InputSchema = doc.RootElement.Clone()
                };
            }

            public Task<CallToolResult> ExecuteAsync(
                JsonDocument? arguments,
                IServiceProvider serviceProvider,
                CancellationToken cancellationToken = default)
            {
                // Not used in these tests
                throw new NotImplementedException();
            }
        }

        private sealed class RetainedMetadataMcpTool : IMcpTool
        {
            private readonly Tool _metadata;

            public RetainedMetadataMcpTool(Tool metadata)
            {
                _metadata = metadata;
            }

            public ToolType ToolType => ToolType.Custom;

            public bool IsEnabled(RuntimeConfig config) => true;

            public Tool GetToolMetadata() => _metadata;

            public Task<CallToolResult> ExecuteAsync(
                JsonDocument? arguments,
                IServiceProvider serviceProvider,
                CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Creates a RuntimeConfig with the specified DmlToolsConfig for testing.
        /// </summary>
        private static RuntimeConfig CreateRuntimeConfig(DmlToolsConfig? dmlTools = null)
        {
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true, Path: "/mcp", DmlTools: dmlTools),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );
        }

        #endregion Private helpers
    }
}
