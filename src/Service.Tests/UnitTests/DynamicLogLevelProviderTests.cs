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
            bool isCliOverridden,
            bool isConfigOverridden,
            string mcpLevel,
            bool expectedResult,
            LogLevel expectedFinalLevel)
        {
            // Arrange
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(initialLevel, isCliOverridden, isConfigOverridden);

            // Act
            bool result = provider.UpdateFromMcp(mcpLevel);

            // Assert
            Assert.AreEqual(expectedResult, result);
            Assert.AreEqual(expectedFinalLevel, provider.CurrentLogLevel);

            // Successful agent updates must flip IsAgentOverridden so hot-reloads of Config
            // don't clobber the agent's choice.
            Assert.AreEqual(expectedResult, provider.IsAgentOverridden);
        }

        [TestMethod]
        public void ShouldLog_ReturnsCorrectResult()
        {
            // Arrange
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Warning, isCliOverridden: false);

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
            provider.SetInitialLogLevel(LogLevel.Information, isCliOverridden: false, isConfigOverridden: false);

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
            provider.SetInitialLogLevel(LogLevel.Warning, isCliOverridden: true, isConfigOverridden: false);

            // Act — flood with MCP setLevel requests; every one must succeed.
            Parallel.For(0, 2_000, _ =>
            {
                bool changed = provider.UpdateFromMcp("debug");
                Assert.IsTrue(changed, "Agent must override CLI and Config.");
            });

            // Assert — final level is the agent-set Debug, and the agent flag is set.
            Assert.AreEqual(LogLevel.Debug, provider.CurrentLogLevel);
            Assert.IsTrue(provider.IsAgentOverridden);
            // CLI flag is informational and stays as set by startup; precedence is enforced
            // via IsAgentOverridden in UpdateFromRuntimeConfig.
            Assert.IsTrue(provider.IsCliOverridden);
        }

        /// <summary>
        /// Hot-reloading the runtime config must not clobber an agent-set level. After the
        /// agent moves the level via <see cref="DynamicLogLevelProvider.UpdateFromMcp"/>, a
        /// subsequent <see cref="DynamicLogLevelProvider.UpdateFromRuntimeConfig"/> with a
        /// different config-pinned level must be ignored.
        /// </summary>
        [TestMethod]
        public void UpdateFromRuntimeConfig_RespectsAgentOverride()
        {
            // Arrange — start at Error (no CLI / Config override), agent then asks for Debug.
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Error, isCliOverridden: false, isConfigOverridden: false);
            Assert.IsTrue(provider.UpdateFromMcp("debug"));
            Assert.AreEqual(LogLevel.Debug, provider.CurrentLogLevel);

            // Build a hot-reloaded RuntimeConfig that explicitly pins log-level to Warning.
            RuntimeConfig hotReloadedConfig = BuildRuntimeConfigWithLogLevel(LogLevel.Warning);

            // Act — the hot-reload guard must skip applying Warning because the agent already won.
            provider.UpdateFromRuntimeConfig(hotReloadedConfig);

            // Assert — agent's Debug survives.
            Assert.AreEqual(LogLevel.Debug, provider.CurrentLogLevel);
            Assert.IsTrue(provider.IsAgentOverridden);
        }

        /// <summary>
        /// Race regression: a runtime-config hot-reload must never overwrite an agent-set
        /// level even when both calls execute concurrently. Without a shared lock, a
        /// <see cref="DynamicLogLevelProvider.UpdateFromRuntimeConfig"/> caller could pass
        /// the <c>IsAgentOverridden</c> guard before <see cref="DynamicLogLevelProvider.UpdateFromMcp"/>
        /// flips the flag, then write the config-pinned level on top of the agent's choice.
        /// </summary>
        [TestMethod]
        public void UpdateFromMcp_BeatsConcurrentConfigHotReload()
        {
            // Arrange — start at Error, then let the agent pin Debug so the flag is set.
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Error, isCliOverridden: false, isConfigOverridden: false);
            Assert.IsTrue(provider.UpdateFromMcp("debug"));

            RuntimeConfig warningConfig = BuildRuntimeConfigWithLogLevel(LogLevel.Warning);

            // Act — flood both update paths in parallel. Every interleaving must end
            // with the agent's Debug because IsAgentOverridden is sticky-true.
            const int iterations = 2_000;
            Task hotReloads = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    provider.UpdateFromRuntimeConfig(warningConfig);
                }
            });

            Task agentSetLevels = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    Assert.IsTrue(provider.UpdateFromMcp("debug"));
                }
            });

            Task.WaitAll(new[] { hotReloads, agentSetLevels }, millisecondsTimeout: 5_000);
            Assert.IsTrue(hotReloads.IsCompletedSuccessfully, $"Hot-reload task failed: {hotReloads.Exception?.Message}");
            Assert.IsTrue(agentSetLevels.IsCompletedSuccessfully, $"Agent task failed: {agentSetLevels.Exception?.Message}");

            // Assert — the agent's level survives every race.
            Assert.AreEqual(LogLevel.Debug, provider.CurrentLogLevel);
            Assert.IsTrue(provider.IsAgentOverridden);
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
