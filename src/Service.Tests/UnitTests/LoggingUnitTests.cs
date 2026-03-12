// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="StartupLogBuffer"/>.
    /// </summary>
    [TestClass]
    public class StartupLogBufferTests
    {
        /// <summary>
        /// Verifies that <see cref="StartupLogBuffer.FlushToLogger"/> replays buffered entries
        /// to the target logger in the order they were enqueued.
        /// </summary>
        [TestMethod]
        public void FlushToLogger_ReplayBufferedEntriesInOrder()
        {
            // Arrange
            StartupLogBuffer buffer = new();
            buffer.BufferLog(LogLevel.Information, "first");
            buffer.BufferLog(LogLevel.Warning, "second");
            buffer.BufferLog(LogLevel.Error, "third");

            List<(LogLevel level, string msg)> received = new();
            Mock<ILogger> mockLogger = new();
            mockLogger
                .Setup(l => l.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback<LogLevel, EventId, object, Exception?, Delegate>((level, _, state, _, formatter) =>
                {
                    received.Add((level, formatter.DynamicInvoke(state, null) as string ?? string.Empty));
                });
            mockLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            // Act
            buffer.FlushToLogger(mockLogger.Object);

            // Assert
            Assert.AreEqual(3, received.Count);
            Assert.AreEqual(LogLevel.Information, received[0].level);
            Assert.AreEqual(LogLevel.Warning, received[1].level);
            Assert.AreEqual(LogLevel.Error, received[2].level);
            StringAssert.Contains(received[0].msg, "first");
            StringAssert.Contains(received[1].msg, "second");
            StringAssert.Contains(received[2].msg, "third");
        }

        /// <summary>
        /// Verifies that <see cref="StartupLogBuffer.FlushToLogger"/> with a null logger
        /// simply discards all buffered entries without throwing.
        /// </summary>
        [TestMethod]
        public void FlushToLogger_NullLogger_DiscardsEntriesWithoutThrowing()
        {
            StartupLogBuffer buffer = new();
            buffer.BufferLog(LogLevel.Information, "message");

            // Should not throw
            buffer.FlushToLogger(null);
        }

        /// <summary>
        /// Verifies that calling <see cref="StartupLogBuffer.FlushToLogger"/> a second time
        /// after the buffer has been drained is a no-op (no duplicate logging).
        /// </summary>
        [TestMethod]
        public void FlushToLogger_SecondCallOnDrainedBuffer_IsNoOp()
        {
            StartupLogBuffer buffer = new();
            buffer.BufferLog(LogLevel.Information, "only once");

            int callCount = 0;
            Mock<ILogger> mockLogger = new();
            mockLogger
                .Setup(l => l.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(() => callCount++);
            mockLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            buffer.FlushToLogger(mockLogger.Object);
            buffer.FlushToLogger(mockLogger.Object); // second flush on empty buffer

            Assert.AreEqual(1, callCount, "The message should have been logged exactly once.");
        }

        /// <summary>
        /// Verifies that an empty buffer produces no log calls.
        /// </summary>
        [TestMethod]
        public void FlushToLogger_EmptyBuffer_NoLogCalls()
        {
            StartupLogBuffer buffer = new();
            Mock<ILogger> mockLogger = new();

            buffer.FlushToLogger(mockLogger.Object);

            mockLogger.Verify(
                l => l.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }
    }

    /// <summary>
    /// Unit tests for <see cref="DynamicLogLevelProvider"/>.
    /// </summary>
    [TestClass]
    public class DynamicLogLevelProviderTests
    {
        /// <summary>
        /// Verifies that <see cref="DynamicLogLevelProvider.SetInitialLogLevel"/> sets the
        /// current log level and the CLI-override flag correctly.
        /// </summary>
        [DataTestMethod]
        [DataRow(LogLevel.Trace, true, DisplayName = "Trace with CLI override")]
        [DataRow(LogLevel.Warning, false, DisplayName = "Warning without CLI override")]
        [DataRow(LogLevel.None, true, DisplayName = "None with CLI override")]
        [DataRow(LogLevel.Error, false, DisplayName = "Error without CLI override")]
        public void SetInitialLogLevel_SetsLevelAndFlag(LogLevel logLevel, bool isCliOverridden)
        {
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(logLevel, isCliOverridden);

            Assert.AreEqual(logLevel, provider.CurrentLogLevel);
            Assert.AreEqual(isCliOverridden, provider.IsCliOverridden);
        }

        /// <summary>
        /// Verifies that <see cref="DynamicLogLevelProvider.UpdateFromRuntimeConfig"/> updates
        /// the log level when the CLI has not overridden it.
        /// </summary>
        [TestMethod]
        public void UpdateFromRuntimeConfig_WithoutCliOverride_UpdatesLevel()
        {
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Error, isCliOverridden: false);

            // Build a RuntimeConfig with a "default" log level of Warning.
            Dictionary<string, LogLevel?> logLevels = new() { { "default", LogLevel.Warning } };
            RuntimeConfig config = BuildConfigWithLogLevel(logLevels);

            provider.UpdateFromRuntimeConfig(config);

            Assert.AreEqual(LogLevel.Warning, provider.CurrentLogLevel);
        }

        /// <summary>
        /// Verifies that <see cref="DynamicLogLevelProvider.UpdateFromRuntimeConfig"/> does NOT
        /// change the log level when the CLI has already overridden it.
        /// </summary>
        [TestMethod]
        public void UpdateFromRuntimeConfig_WithCliOverride_KeepsCliLevel()
        {
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.None, isCliOverridden: true);

            // Config would set Warning, but CLI None must win.
            Dictionary<string, LogLevel?> logLevels = new() { { "default", LogLevel.Warning } };
            RuntimeConfig config = BuildConfigWithLogLevel(logLevels);

            provider.UpdateFromRuntimeConfig(config);

            Assert.AreEqual(LogLevel.None, provider.CurrentLogLevel);
        }

        /// <summary>
        /// Verifies that <see cref="DynamicLogLevelProvider.ShouldLog"/> returns true only
        /// for messages at or above <see cref="DynamicLogLevelProvider.CurrentLogLevel"/>.
        /// </summary>
        [DataTestMethod]
        [DataRow(LogLevel.Warning, LogLevel.Trace, false, DisplayName = "Trace suppressed at Warning threshold")]
        [DataRow(LogLevel.Warning, LogLevel.Debug, false, DisplayName = "Debug suppressed at Warning threshold")]
        [DataRow(LogLevel.Warning, LogLevel.Information, false, DisplayName = "Info suppressed at Warning threshold")]
        [DataRow(LogLevel.Warning, LogLevel.Warning, true, DisplayName = "Warning passes Warning threshold")]
        [DataRow(LogLevel.Warning, LogLevel.Error, true, DisplayName = "Error passes Warning threshold")]
        [DataRow(LogLevel.Warning, LogLevel.Critical, true, DisplayName = "Critical passes Warning threshold")]
        [DataRow(LogLevel.None, LogLevel.Critical, false, DisplayName = "Critical suppressed at None threshold")]
        [DataRow(LogLevel.Trace, LogLevel.Trace, true, DisplayName = "Trace passes Trace threshold")]
        public void ShouldLog_ReturnsCorrectResult(LogLevel threshold, LogLevel messageLevel, bool expected)
        {
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(threshold);

            Assert.AreEqual(expected, provider.ShouldLog(messageLevel));
        }

        // ------------------------------------------------------------------ helpers

        private static RuntimeConfig BuildConfigWithLogLevel(Dictionary<string, LogLevel?> logLevels)
        {
            TelemetryOptions telemetry = new(
                ApplicationInsights: null,
                OpenTelemetry: null,
                LoggerLevel: logLevels);
            RuntimeOptions runtimeOptions = new(
                Rest: null,
                GraphQL: null,
                Host: new HostOptions(
                    Cors: null,
                    Authentication: new AuthenticationOptions(Provider: "Unauthenticated"),
                    Mode: HostMode.Production),
                BaseRoute: null,
                Telemetry: telemetry,
                Cache: null,
                Pagination: null,
                Mcp: null);
            return new RuntimeConfig(
                Schema: "https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json",
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty, Options: null),
                Runtime: runtimeOptions,
                Entities: new(new Dictionary<string, Entity>()));
        }
    }

    /// <summary>
    /// Unit tests for <see cref="RuntimeConfig.GetConfiguredLogLevel"/> that cover the
    /// case-insensitive "default" key fix.
    /// </summary>
    [TestClass]
    public class GetConfiguredLogLevelTests
    {
        /// <summary>
        /// Verifies that both lowercase "default" and title-case "Default" are resolved
        /// correctly as the fallback log level.
        /// </summary>
        [DataTestMethod]
        [DataRow("default", LogLevel.Warning, DisplayName = "Lowercase 'default' key is resolved")]
        [DataRow("Default", LogLevel.Warning, DisplayName = "Title-case 'Default' key is resolved (case-insensitive fix)")]
        [DataRow("DEFAULT", LogLevel.Warning, DisplayName = "All-caps 'DEFAULT' key is resolved (case-insensitive fix)")]
        public void GetConfiguredLogLevel_DefaultKeyVariants_ReturnExpectedLevel(string defaultKey, LogLevel expectedLevel)
        {
            Dictionary<string, LogLevel?> logLevels = new() { { defaultKey, expectedLevel } };
            RuntimeConfig config = BuildConfigWithLogLevel(logLevels);

            LogLevel actual = config.GetConfiguredLogLevel();
            Assert.AreEqual(expectedLevel, actual);
        }

        /// <summary>
        /// Verifies that a more-specific filter takes precedence over the "default" fallback.
        /// </summary>
        [TestMethod]
        public void GetConfiguredLogLevel_SpecificFilterTakesPrecedenceOverDefault()
        {
            Dictionary<string, LogLevel?> logLevels = new()
            {
                { "Default", LogLevel.Error },
                { "Azure.DataApiBuilder", LogLevel.Debug }
            };
            RuntimeConfig config = BuildConfigWithLogLevel(logLevels);

            LogLevel actual = config.GetConfiguredLogLevel("Azure.DataApiBuilder.Core");
            Assert.AreEqual(LogLevel.Debug, actual, "More specific 'Azure.DataApiBuilder' filter should win over 'Default'.");
        }

        private static RuntimeConfig BuildConfigWithLogLevel(Dictionary<string, LogLevel?> logLevels)
        {
            TelemetryOptions telemetry = new(
                ApplicationInsights: null,
                OpenTelemetry: null,
                LoggerLevel: logLevels);
            RuntimeOptions runtimeOptions = new(
                Rest: null,
                GraphQL: null,
                Host: new HostOptions(
                    Cors: null,
                    Authentication: new AuthenticationOptions(Provider: "Unauthenticated"),
                    Mode: HostMode.Production),
                BaseRoute: null,
                Telemetry: telemetry,
                Cache: null,
                Pagination: null,
                Mcp: null);
            return new RuntimeConfig(
                Schema: "https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json",
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty, Options: null),
                Runtime: runtimeOptions,
                Entities: new(new Dictionary<string, Entity>()));
        }
    }
}
