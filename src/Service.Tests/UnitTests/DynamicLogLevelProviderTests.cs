// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

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
    }
}
