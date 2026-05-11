// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the DynamicLogLevelProvider class.
    /// Tests the MCP logging/setLevel support.
    /// </summary>
    [TestClass]
    public class DynamicLogLevelProviderTests
    {
        [DataTestMethod]
        [DataRow(LogLevel.Error, false, false, "debug", true, LogLevel.Debug, DisplayName = "Valid level change succeeds")]
        [DataRow(LogLevel.Error, true, false, "debug", false, LogLevel.Error, DisplayName = "CLI override blocks MCP change")]
        [DataRow(LogLevel.Warning, false, true, "debug", false, LogLevel.Warning, DisplayName = "Config override blocks MCP change")]
        [DataRow(LogLevel.Error, false, false, "invalid", false, LogLevel.Error, DisplayName = "Invalid level returns false")]
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
        /// CLI override is sticky: once the CLI flag pins the level, no number
        /// of MCP <c>logging/setLevel</c> requests (even concurrent) may change
        /// <see cref="DynamicLogLevelProvider.CurrentLogLevel"/>. Validates the
        /// precedence guarantee under contention.
        /// </summary>
        [TestMethod]
        public void UpdateFromMcp_CliOverride_StaysStickyUnderConcurrency()
        {
            // Arrange — CLI pins level to Warning.
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Warning, isCliOverridden: true, isConfigOverridden: false);

            // Act — flood with MCP setLevel requests trying to flip it.
            Parallel.For(0, 2_000, _ =>
            {
                bool changed = provider.UpdateFromMcp("debug");
                Assert.IsFalse(changed, "CLI override must block all MCP changes.");
            });

            // Assert — level never moved off Warning.
            Assert.AreEqual(LogLevel.Warning, provider.CurrentLogLevel);
        }
    }
}
