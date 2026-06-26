// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the DynamicLogLevelProvider class.
    /// Locks the runtime log-level precedence: Agent (MCP) > CLI > Config > defaults.
    /// </summary>
    [TestClass]
    public class DynamicLogLevelProviderTests
    {
        [DataTestMethod]
        [DataRow(LogLevel.Error, false, false, "debug", true, LogLevel.Debug, DisplayName = "Valid level change succeeds")]
        [DataRow(LogLevel.Error, true, false, "debug", true, LogLevel.Debug, DisplayName = "Agent overrides CLI")]
        [DataRow(LogLevel.Warning, false, true, "debug", true, LogLevel.Debug, DisplayName = "Agent overrides Config")]
        [DataRow(LogLevel.Error, true, true, "debug", true, LogLevel.Debug, DisplayName = "Agent overrides CLI + Config")]
        [DataRow(LogLevel.Error, false, false, "invalid", false, LogLevel.Error, DisplayName = "Invalid level returns false and leaves level untouched")]
        public void UpdateFromMcp_ReturnsExpectedResult(
            LogLevel initialLevel,
            bool isCliOverriding,
            bool isConfigOverriding,
            string mcpLevel,
            bool expectedResult,
            LogLevel expectedFinalLevel)
        {
            // Arrange
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(initialLevel, isCliOverriding, isConfigOverriding);

            // Act
            bool result = provider.UpdateFromMcp(mcpLevel);

            // Assert
            Assert.AreEqual(expectedResult, result);
            Assert.AreEqual(expectedFinalLevel, provider.CurrentLogLevel);

            // Successful agent updates must flip IsAgentOverriding so hot-reloads of Config
            // don't overwrite the agent's choice.
            Assert.AreEqual(expectedResult, provider.IsAgentOverriding);
        }

        [TestMethod]
        public void ShouldLog_ReturnsCorrectResult()
        {
            // Arrange
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Warning, isCliOverriding: false);

            // Assert - logs at or above Warning should pass
            Assert.IsTrue(provider.ShouldLog(LogLevel.Warning));
            Assert.IsTrue(provider.ShouldLog(LogLevel.Error));
            Assert.IsFalse(provider.ShouldLog(LogLevel.Debug));
        }

        /// <summary>
        /// Concurrency safety: many threads racing on
        /// <see cref="DynamicLogLevelProvider.UpdateFromMcp(string)"/> and
        /// <see cref="DynamicLogLevelProvider.ShouldLog(LogLevel)"/> must not
        /// produce torn reads, exceptions, or corrupted state. The provider
        /// stores state in atomic-sized fields (enum + bools), so reads/writes
        /// are inherently safe; this test guards against future regressions
        /// (e.g., introducing a non-atomic field) by exercising the contract.
        /// </summary>
        [TestMethod]
        public void UpdateFromMcp_UnderConcurrency_StaysConsistent()
        {
            // Arrange
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Information, isCliOverriding: false, isConfigOverriding: false);

            const int iterations = 5_000;

            // Act — alternating writers + readers in parallel.
            Task writers = Task.Run(() =>
            {
                string[] levels = new[] { "debug", "info", "warning", "error" };
                for (int i = 0; i < iterations; i++)
                {
                    provider.UpdateFromMcp(levels[i % levels.Length]);
                }
            });

            Task readers = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    // Read every property — must never throw or read
                    // an enum value outside the LogLevel range.
                    LogLevel current = provider.CurrentLogLevel;
                    bool _ = provider.ShouldLog(LogLevel.Information);
                    Assert.IsTrue(
                        current >= LogLevel.Trace && current <= LogLevel.None,
                        $"CurrentLogLevel out of range: {(int)current}");
                }
            });

            // Assert — both tasks complete cleanly.
            Task.WaitAll(new[] { writers, readers }, millisecondsTimeout: 5_000);
            Assert.IsTrue(writers.IsCompletedSuccessfully, $"Writers task failed: {writers.Exception?.Message}");
            Assert.IsTrue(readers.IsCompletedSuccessfully, $"Readers task failed: {readers.Exception?.Message}");

            // Final state is one of the four levels — exact value is
            // race-dependent but it must be a valid level.
            Assert.IsTrue(
                provider.CurrentLogLevel == LogLevel.Debug ||
                provider.CurrentLogLevel == LogLevel.Information ||
                provider.CurrentLogLevel == LogLevel.Warning ||
                provider.CurrentLogLevel == LogLevel.Error,
                $"Unexpected final level: {provider.CurrentLogLevel}");
        }

        /// <summary>
        /// The agent always wins. Even when the CLI flag has pinned the level, every MCP
        /// <c>logging/setLevel</c> call must succeed and update
        /// <see cref="DynamicLogLevelProvider.CurrentLogLevel"/>. Validates the
        /// new precedence guarantee under contention.
        /// </summary>
        [TestMethod]
        public void UpdateFromMcp_AgentAlwaysWins_UnderConcurrency()
        {
            // Arrange — CLI pins level to Warning.
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Warning, isCliOverriding: true, isConfigOverriding: false);

            // Act — flood with MCP setLevel requests; every one must succeed.
            Parallel.For(0, 2_000, _ =>
            {
                bool changed = provider.UpdateFromMcp("debug");
                Assert.IsTrue(changed, "Agent must override CLI and Config.");
            });

            // Assert — final level is the agent-set Debug, and the agent flag is set.
            Assert.AreEqual(LogLevel.Debug, provider.CurrentLogLevel);
            Assert.IsTrue(provider.IsAgentOverriding);
            // CLI flag is informational and stays as set by startup; precedence is enforced
            // via IsAgentOverriding in UpdateFromRuntimeConfig.
            Assert.IsTrue(provider.IsCliOverriding);
        }

        /// <summary>
        /// Hot-reloading the runtime config must not overwrite an agent-set level. After the
        /// agent moves the level via <see cref="DynamicLogLevelProvider.UpdateFromMcp"/>, a
        /// subsequent <see cref="DynamicLogLevelProvider.UpdateFromRuntimeConfig"/> with a
        /// different config-pinned level must be ignored.
        /// </summary>
        [TestMethod]
        public void UpdateFromRuntimeConfig_RespectsAgentOverride()
        {
            // Arrange — start at Error (no CLI / Config override), agent then asks for Debug.
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Error, isCliOverriding: false, isConfigOverriding: false);
            Assert.IsTrue(provider.UpdateFromMcp("debug"));
            Assert.AreEqual(LogLevel.Debug, provider.CurrentLogLevel);

            // Build a hot-reloaded RuntimeConfig that explicitly pins log-level to Warning.
            RuntimeConfig hotReloadedConfig = BuildRuntimeConfigWithLogLevel(LogLevel.Warning);

            // Act — the hot-reload guard must skip applying Warning because the agent already won.
            provider.UpdateFromRuntimeConfig(hotReloadedConfig);

            // Assert — agent's Debug survives.
            Assert.AreEqual(LogLevel.Debug, provider.CurrentLogLevel);
            Assert.IsTrue(provider.IsAgentOverriding);
        }

        /// <summary>
        /// Hot-reloading the runtime config must not overwrite a CLI-set level. The CLI
        /// <c>--log-level</c> flag is the operator's deliberate startup choice, so a
        /// subsequent <see cref="DynamicLogLevelProvider.UpdateFromRuntimeConfig"/> with a
        /// different config-pinned level must be ignored.
        /// </summary>
        [TestMethod]
        public void UpdateFromRuntimeConfig_RespectsCliOverride()
        {
            // Arrange — CLI pins level to Warning at startup; no agent override.
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Warning, isCliOverriding: true, isConfigOverriding: false);

            // Build a hot-reloaded RuntimeConfig that explicitly pins log-level to Information.
            RuntimeConfig hotReloadedConfig = BuildRuntimeConfigWithLogLevel(LogLevel.Information);

            // Act — the hot-reload guard must skip applying Information because CLI already won.
            provider.UpdateFromRuntimeConfig(hotReloadedConfig);

            // Assert — CLI's Warning survives, and IsConfigOverriding is not flipped because
            // the hot-reload short-circuited before reading the config's log level.
            Assert.AreEqual(LogLevel.Warning, provider.CurrentLogLevel);
            Assert.IsTrue(provider.IsCliOverriding);
            Assert.IsFalse(provider.IsConfigOverriding);
        }

        /// <summary>
        /// Successive agent calls must each succeed and overwrite the previous agent-set level
        /// — covering the full range from <see cref="LogLevel.Debug"/> (most verbose) to
        /// <see cref="LogLevel.Critical"/> (most restrictive). This locks in the sticky
        /// behavior of <see cref="DynamicLogLevelProvider.IsAgentOverriding"/>: once the agent
        /// has taken control, every subsequent agent call wins, the flag stays set, and no
        /// hot-reload of the runtime config can sneak in between calls to silently flip the
        /// level back. The MCP spec doesn't define a "none" level, so the strictest valid
        /// MCP level <c>"emergency"</c> (which maps to <see cref="LogLevel.Critical"/>) is
        /// used as the high-end of the range.
        /// </summary>
        [TestMethod]
        public void UpdateFromMcp_SuccessiveCalls_OverwriteAndKeepAgentOverriding()
        {
            // Arrange — start in the "no overrides" state.
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Information, isCliOverriding: false, isConfigOverriding: false);

            // Act + Assert — Debug → Critical (verbose to most-restrictive).
            Assert.IsTrue(provider.UpdateFromMcp("debug"));
            Assert.AreEqual(LogLevel.Debug, provider.CurrentLogLevel);
            Assert.IsTrue(provider.IsAgentOverriding);

            Assert.IsTrue(provider.UpdateFromMcp("emergency"));
            Assert.AreEqual(LogLevel.Critical, provider.CurrentLogLevel);
            Assert.IsTrue(provider.IsAgentOverriding, "Agent override must remain sticky across successive agent calls.");

            // Act + Assert — Critical → Debug (reverse direction).
            Assert.IsTrue(provider.UpdateFromMcp("debug"));
            Assert.AreEqual(LogLevel.Debug, provider.CurrentLogLevel);
            Assert.IsTrue(provider.IsAgentOverriding);
        }

        /// <summary>
        /// Builds a minimal <see cref="RuntimeConfig"/> whose
        /// <c>runtime.telemetry.log-level</c> dictionary explicitly pins the default level.
        /// Used by hot-reload tests; not intended to model a complete production config.
        /// </summary>
        private static RuntimeConfig BuildRuntimeConfigWithLogLevel(LogLevel level)
        {
            TelemetryOptions telemetry = new(
                LoggerLevel: new Dictionary<string, LogLevel?> { ["default"] = level });

            RuntimeOptions runtime = new(
                Rest: null,
                GraphQL: null,
                Mcp: null,
                Host: new HostOptions(Cors: null, Authentication: null, Mode: HostMode.Production),
                Telemetry: telemetry);

            DataSource dataSource = new(DatabaseType.MSSQL, "Server=tcp:localhost,1433;", Options: null);

            return new RuntimeConfig(
                Schema: null,
                DataSource: dataSource,
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
                Runtime: runtime);
        }
    }
}
