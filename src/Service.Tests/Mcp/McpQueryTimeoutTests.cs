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
    /// Tests for the MCP query-timeout configuration property.
    /// Verifies:
    /// - Default value of 30 seconds when not configured
    /// - Custom value overrides default
    /// - Timeout wrapping applies to all MCP tools via ExecuteWithTelemetryAsync
    /// - Hot reload: changing config value updates behavior without restart
    /// - Timeout surfaces as TimeoutException, not generic cancellation
    /// - Telemetry maps timeout to TIMEOUT error code
    /// </summary>
    [TestClass]
    public class McpQueryTimeoutTests
    {
        #region Custom Value Tests

        [DataTestMethod]
        [DataRow(1, DisplayName = "1 second")]
        [DataRow(60, DisplayName = "60 seconds")]
        [DataRow(120, DisplayName = "120 seconds")]
        public void McpRuntimeOptions_CustomTimeout_ReturnsConfiguredValue(int timeoutSeconds)
        {
            McpRuntimeOptions options = new(QueryTimeout: timeoutSeconds);
            Assert.AreEqual(timeoutSeconds, options.EffectiveQueryTimeoutSeconds);
        }

        [TestMethod]
        public void RuntimeConfig_McpQueryTimeout_ExposedInConfig()
        {
            RuntimeConfig config = CreateConfig(queryTimeout: 45);
            Assert.AreEqual(45, config.Runtime?.Mcp?.QueryTimeout);
            Assert.AreEqual(45, config.Runtime?.Mcp?.EffectiveQueryTimeoutSeconds);
        }

        [TestMethod]
        public void RuntimeConfig_McpQueryTimeout_DefaultWhenNotSet()
        {
            RuntimeConfig config = CreateConfig();
            Assert.IsNull(config.Runtime?.Mcp?.QueryTimeout);
            Assert.AreEqual(30, config.Runtime?.Mcp?.EffectiveQueryTimeoutSeconds);
        }

        #endregion

        #region Timeout Wrapping Tests

        [TestMethod]
        public async Task ExecuteWithTelemetry_CompletesSuccessfully_WithinTimeout()
        {
            // A tool that completes immediately should succeed
            RuntimeConfig config = CreateConfig(queryTimeout: 30);
            IServiceProvider sp = CreateServiceProviderWithConfig(config);
            IMcpTool tool = new ImmediateCompletionTool();

            CallToolResult result = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool, "test_tool", null, sp, CancellationToken.None);

            // Tool should complete without throwing TimeoutException
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsError != true, "Tool result should not be an error");
        }

        [TestMethod]
        public async Task ExecuteWithTelemetry_ThrowsTimeoutException_WhenToolExceedsTimeout()
        {
            // Configure a very short timeout (1 second) and a tool that takes longer
            RuntimeConfig config = CreateConfig(queryTimeout: 1);
            IServiceProvider sp = CreateServiceProviderWithConfig(config);
            IMcpTool tool = new SlowTool(delaySeconds: 30);

            await Assert.ThrowsExceptionAsync<TimeoutException>(async () =>
            {
                await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                    tool, "slow_tool", null, sp, CancellationToken.None);
            });
        }

        [TestMethod]
        public async Task ExecuteWithTelemetry_TimeoutMessage_ContainsToolName()
        {
            RuntimeConfig config = CreateConfig(queryTimeout: 1);
            IServiceProvider sp = CreateServiceProviderWithConfig(config);
            IMcpTool tool = new SlowTool(delaySeconds: 30);

            try
            {
                await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                    tool, "aggregate_records", null, sp, CancellationToken.None);
                Assert.Fail("Expected TimeoutException");
            }
            catch (TimeoutException ex)
            {
                Assert.IsTrue(ex.Message.Contains("aggregate_records"), "Message should contain tool name");
                Assert.IsTrue(ex.Message.Contains("1 second"), "Message should contain timeout value");
                Assert.IsTrue(ex.Message.Contains("NOT a tool error"), "Message should clarify it is not a tool error");
            }
        }

        [TestMethod]
        public async Task ExecuteWithTelemetry_ClientCancellation_PropagatesAsCancellation()
        {
            // Client cancellation (not timeout) should propagate as OperationCanceledException
            // rather than being converted to TimeoutException.
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
                // (or subclass TaskCanceledException)
            }
        }

        [TestMethod]
        public async Task ExecuteWithTelemetry_AppliesTimeout_ToAllToolTypes()
        {
            // Verify timeout applies to both built-in and custom tool types
            RuntimeConfig config = CreateConfig(queryTimeout: 1);
            IServiceProvider sp = CreateServiceProviderWithConfig(config);

            // Test with built-in tool type
            IMcpTool builtInTool = new SlowTool(delaySeconds: 30, toolType: ToolType.BuiltIn);
            await Assert.ThrowsExceptionAsync<TimeoutException>(async () =>
            {
                await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                    builtInTool, "builtin_slow", null, sp, CancellationToken.None);
            });

            // Test with custom tool type
            IMcpTool customTool = new SlowTool(delaySeconds: 30, toolType: ToolType.Custom);
            await Assert.ThrowsExceptionAsync<TimeoutException>(async () =>
            {
                await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                    customTool, "custom_slow", null, sp, CancellationToken.None);
            });
        }

        #endregion

        #region Hot Reload Tests

        [TestMethod]
        public async Task ExecuteWithTelemetry_ReadsConfigPerInvocation_HotReload()
        {
            // First invocation with long timeout should succeed
            RuntimeConfig config1 = CreateConfig(queryTimeout: 30);
            IServiceProvider sp1 = CreateServiceProviderWithConfig(config1);

            IMcpTool fastTool = new ImmediateCompletionTool();
            CallToolResult result1 = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                fastTool, "test_tool", null, sp1, CancellationToken.None);
            Assert.IsNotNull(result1);

            // Second invocation with very short timeout and a slow tool should timeout.
            // This demonstrates that each invocation reads the current config value.
            RuntimeConfig config2 = CreateConfig(queryTimeout: 1);
            IServiceProvider sp2 = CreateServiceProviderWithConfig(config2);

            IMcpTool slowTool = new SlowTool(delaySeconds: 30);
            await Assert.ThrowsExceptionAsync<TimeoutException>(async () =>
            {
                await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                    slowTool, "test_tool", null, sp2, CancellationToken.None);
            });
        }

        #endregion

        // Note: MapExceptionToErrorCode tests are in McpTelemetryTests (covers all exception types via DataRow).

        #region JSON Serialization Tests

        [TestMethod]
        public void McpRuntimeOptions_Serialization_IncludesQueryTimeout_WhenUserProvided()
        {
            McpRuntimeOptions options = new(QueryTimeout: 45);
            JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions();
            string json = JsonSerializer.Serialize(options, serializerOptions);
            Assert.IsTrue(json.Contains("\"query-timeout\": 45") || json.Contains("\"query-timeout\":45"));
        }

        [TestMethod]
        public void McpRuntimeOptions_Deserialization_ReadsQueryTimeout()
        {
            string json = @"{""enabled"": true, ""query-timeout"": 60}";
            JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions();
            McpRuntimeOptions options = JsonSerializer.Deserialize<McpRuntimeOptions>(json, serializerOptions);
            Assert.IsNotNull(options);
            Assert.AreEqual(60, options.QueryTimeout);
            Assert.AreEqual(60, options.EffectiveQueryTimeoutSeconds);
        }

        [TestMethod]
        public void McpRuntimeOptions_Deserialization_DefaultsWhenOmitted()
        {
            string json = @"{""enabled"": true}";
            JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions();
            McpRuntimeOptions options = JsonSerializer.Deserialize<McpRuntimeOptions>(json, serializerOptions);
            Assert.IsNotNull(options);
            Assert.IsNull(options.QueryTimeout);
            Assert.AreEqual(30, options.EffectiveQueryTimeoutSeconds);
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
                        QueryTimeout: queryTimeout,
                        DmlTools: new(
                            describeEntities: true,
                            readRecords: true,
                            createRecord: true,
                            updateRecord: true,
                            deleteRecord: true,
                            executeEntity: true,
                            aggregateRecords: true
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
        /// Used to test timeout behavior.
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
