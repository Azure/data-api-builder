// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Tests for the aggregate-records query-timeout configuration property.
    /// Verifies:
    /// - Default value of 30 seconds when not configured
    /// - Custom value overrides default
    /// - DmlToolsConfig properties reflect configured timeout
    /// - JSON serialization/deserialization of aggregate-records with query-timeout
    /// </summary>
    [TestClass]
    public class McpQueryTimeoutTests
    {
        #region Custom Value Tests

        [DataTestMethod]
        [DataRow(1, DisplayName = "1 second")]
        [DataRow(60, DisplayName = "60 seconds")]
        [DataRow(120, DisplayName = "120 seconds")]
        public void DmlToolsConfig_CustomTimeout_ReturnsConfiguredValue(int timeoutSeconds)
        {
            DmlToolsConfig config = new(aggregateRecordsQueryTimeout: timeoutSeconds);
            Assert.AreEqual(timeoutSeconds, config.EffectiveAggregateRecordsQueryTimeoutSeconds);
            Assert.IsTrue(config.UserProvidedAggregateRecordsQueryTimeout);
        }

        [TestMethod]
        public void RuntimeConfig_AggregateRecordsQueryTimeout_ExposedInConfig()
        {
            RuntimeConfig config = CreateConfig(queryTimeout: 45);
            Assert.AreEqual(45, config.Runtime?.Mcp?.DmlTools?.AggregateRecordsQueryTimeout);
            Assert.AreEqual(45, config.Runtime?.Mcp?.DmlTools?.EffectiveAggregateRecordsQueryTimeoutSeconds);
        }

        [TestMethod]
        public void RuntimeConfig_AggregateRecordsQueryTimeout_DefaultWhenNotSet()
        {
            RuntimeConfig config = CreateConfig();
            Assert.IsNull(config.Runtime?.Mcp?.DmlTools?.AggregateRecordsQueryTimeout);
            Assert.AreEqual(DmlToolsConfig.DEFAULT_QUERY_TIMEOUT_SECONDS, config.Runtime?.Mcp?.DmlTools?.EffectiveAggregateRecordsQueryTimeoutSeconds);
        }

        #endregion

        #region Telemetry No-Timeout Tests

        [TestMethod]
        public async Task ExecuteWithTelemetry_CompletesSuccessfully_NoTimeout()
        {
            // After moving timeout to AggregateRecordsTool, ExecuteWithTelemetryAsync should
            // no longer apply any timeout wrapping. A fast tool should complete regardless of config.
            RuntimeConfig config = CreateConfig(queryTimeout: 1);
            IServiceProvider sp = CreateServiceProviderWithConfig(config);
            IMcpTool tool = new ImmediateCompletionTool();

            CallToolResult result = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool, "test_tool", null, sp, CancellationToken.None);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsError != true, "Tool result should not be an error");
        }

        [TestMethod]
        public async Task ExecuteWithTelemetry_DoesNotApplyTimeout_AfterRefactor()
        {
            // Verify that McpTelemetryHelper no longer applies timeout wrapping.
            // A slow tool should NOT timeout in the telemetry layer (timeout is now tool-specific).
            RuntimeConfig config = CreateConfig(queryTimeout: 1);
            IServiceProvider sp = CreateServiceProviderWithConfig(config);

            // Use a short-delay tool (2 seconds) with 1-second query-timeout.
            // If McpTelemetryHelper still applied timeout, this would throw TimeoutException.
            IMcpTool tool = new SlowTool(delaySeconds: 2);

            // Should complete without timeout since McpTelemetryHelper no longer wraps with timeout
            CallToolResult result = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool, "test_tool", null, sp, CancellationToken.None);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsError != true, "Tool should complete without timeout in telemetry layer");
        }

        [TestMethod]
        public async Task ExecuteWithTelemetry_ClientCancellation_PropagatesAsCancellation()
        {
            // Client cancellation should still propagate as OperationCanceledException.
            RuntimeConfig config = CreateConfig(queryTimeout: 30);
            IServiceProvider sp = CreateServiceProviderWithConfig(config);
            IMcpTool tool = new SlowTool(delaySeconds: 30);

            using CancellationTokenSource cts = new();
            cts.Cancel(); // Cancel immediately

            try
            {
                await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                    tool, "test_tool", null, sp, cts.Token);
                Assert.Fail("Expected OperationCanceledException or subclass to be thrown");
            }
            catch (TimeoutException)
            {
                Assert.Fail("Client cancellation should NOT be converted to TimeoutException");
            }
            catch (OperationCanceledException)
            {
                // Expected: client-initiated cancellation propagates as OperationCanceledException
            }
        }

        #endregion

        #region JSON Serialization Tests

        [TestMethod]
        public void DmlToolsConfig_Serialization_IncludesQueryTimeout_WhenUserProvided()
        {
            // When aggregate-records has a query-timeout, it should serialize as object format
            DmlToolsConfig dmlTools = new(aggregateRecords: true, aggregateRecordsQueryTimeout: 45);
            McpRuntimeOptions options = new(Enabled: true, DmlTools: dmlTools);
            JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions();
            string json = JsonSerializer.Serialize(options, serializerOptions);
            Assert.IsTrue(json.Contains("\"query-timeout\""), $"Expected 'query-timeout' in JSON. Got: {json}");
            Assert.IsTrue(json.Contains("45"), $"Expected timeout value 45 in JSON. Got: {json}");
        }

        [TestMethod]
        public void DmlToolsConfig_Deserialization_ReadsQueryTimeout_ObjectFormat()
        {
            string json = @"{""enabled"": true, ""dml-tools"": { ""aggregate-records"": { ""enabled"": true, ""query-timeout"": 60 } }}";
            JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions();
            McpRuntimeOptions options = JsonSerializer.Deserialize<McpRuntimeOptions>(json, serializerOptions);
            Assert.IsNotNull(options);
            Assert.IsNotNull(options.DmlTools);
            Assert.AreEqual(true, options.DmlTools.AggregateRecords);
            Assert.AreEqual(60, options.DmlTools.AggregateRecordsQueryTimeout);
            Assert.AreEqual(60, options.DmlTools.EffectiveAggregateRecordsQueryTimeoutSeconds);
        }

        [TestMethod]
        public void DmlToolsConfig_Deserialization_AggregateRecordsBoolean_NoQueryTimeout()
        {
            string json = @"{""enabled"": true, ""dml-tools"": { ""aggregate-records"": true }}";
            JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions();
            McpRuntimeOptions options = JsonSerializer.Deserialize<McpRuntimeOptions>(json, serializerOptions);
            Assert.IsNotNull(options);
            Assert.IsNotNull(options.DmlTools);
            Assert.AreEqual(true, options.DmlTools.AggregateRecords);
            Assert.IsNull(options.DmlTools.AggregateRecordsQueryTimeout);
            Assert.AreEqual(DmlToolsConfig.DEFAULT_QUERY_TIMEOUT_SECONDS, options.DmlTools.EffectiveAggregateRecordsQueryTimeoutSeconds);
        }

        [TestMethod]
        public void DmlToolsConfig_Deserialization_DefaultsWhenOmitted()
        {
            string json = @"{""enabled"": true}";
            JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions();
            McpRuntimeOptions options = JsonSerializer.Deserialize<McpRuntimeOptions>(json, serializerOptions);
            Assert.IsNotNull(options);
            Assert.IsNull(options.DmlTools?.AggregateRecordsQueryTimeout);
            Assert.AreEqual(DmlToolsConfig.DEFAULT_QUERY_TIMEOUT_SECONDS, options.DmlTools?.EffectiveAggregateRecordsQueryTimeoutSeconds ?? DmlToolsConfig.DEFAULT_QUERY_TIMEOUT_SECONDS);
        }

        #endregion

        #region Helpers

        private static RuntimeConfig CreateConfig(int? queryTimeout = null)
        {
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(
                        Enabled: true,
                        Path: "/mcp",
                        DmlTools: new(
                            describeEntities: true,
                            readRecords: true,
                            createRecord: true,
                            updateRecord: true,
                            deleteRecord: true,
                            executeEntity: true,
                            aggregateRecords: true,
                            aggregateRecordsQueryTimeout: queryTimeout
                        )
                    ),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );
        }

        private static IServiceProvider CreateServiceProviderWithConfig(RuntimeConfig config)
        {
            ServiceCollection services = new();
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            services.AddSingleton(configProvider);
            services.AddLogging();
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// A mock tool that completes immediately with a success result.
        /// </summary>
        private class ImmediateCompletionTool : IMcpTool
        {
            public ToolType ToolType { get; } = ToolType.BuiltIn;

            public Tool GetToolMetadata()
            {
                using JsonDocument doc = JsonDocument.Parse("{\"type\": \"object\"}");
                return new Tool
                {
                    Name = "test_tool",
                    Description = "A test tool that completes immediately",
                    InputSchema = doc.RootElement.Clone()
                };
            }

            public Task<CallToolResult> ExecuteAsync(
                JsonDocument arguments,
                IServiceProvider serviceProvider,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new CallToolResult
                {
                    Content = new List<ContentBlock>
                    {
                        new TextContentBlock { Text = "{\"result\": \"success\"}" }
                    }
                });
            }
        }

        /// <summary>
        /// A mock tool that delays for a specified duration, respecting cancellation.
        /// Used to test cancellation behavior.
        /// </summary>
        private class SlowTool : IMcpTool
        {
            private readonly int _delaySeconds;

            public SlowTool(int delaySeconds, ToolType toolType = ToolType.BuiltIn)
            {
                _delaySeconds = delaySeconds;
                ToolType = toolType;
            }

            public ToolType ToolType { get; }

            public Tool GetToolMetadata()
            {
                using JsonDocument doc = JsonDocument.Parse("{\"type\": \"object\"}");
                return new Tool
                {
                    Name = "slow_tool",
                    Description = "A test tool that takes a long time",
                    InputSchema = doc.RootElement.Clone()
                };
            }

            public async Task<CallToolResult> ExecuteAsync(
                JsonDocument arguments,
                IServiceProvider serviceProvider,
                CancellationToken cancellationToken = default)
            {
                await Task.Delay(TimeSpan.FromSeconds(_delaySeconds), cancellationToken);
                return new CallToolResult
                {
                    Content = new List<ContentBlock>
                    {
                        new TextContentBlock { Text = "{\"result\": \"completed\"}" }
                    }
                };
            }
        }

        #endregion
    }
}
