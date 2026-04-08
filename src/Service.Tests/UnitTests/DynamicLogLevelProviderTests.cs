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
        [TestMethod]
        public void UpdateFromMcp_ValidLevel_ChangesLogLevel()
        {
            // Arrange
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Error, isCliOverridden: false);

            // Act
            bool result = provider.UpdateFromMcp("debug");

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(LogLevel.Debug, provider.CurrentLogLevel);
        }

        [TestMethod]
        public void UpdateFromMcp_CliOverridden_DoesNotChangeLogLevel()
        {
            // Arrange
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Error, isCliOverridden: true);

            // Act
            bool result = provider.UpdateFromMcp("debug");

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(LogLevel.Error, provider.CurrentLogLevel);
        }

        [TestMethod]
        public void UpdateFromMcp_ConfigOverridden_DoesNotChangeLogLevel()
        {
            // Arrange
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Warning, isCliOverridden: false, isConfigOverridden: true);

            // Act
            bool result = provider.UpdateFromMcp("debug");

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(LogLevel.Warning, provider.CurrentLogLevel);
        }

        [TestMethod]
        public void UpdateFromMcp_InvalidLevel_ReturnsFalse()
        {
            // Arrange
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Error, isCliOverridden: false);

            // Act
            bool result = provider.UpdateFromMcp("invalid");

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(LogLevel.Error, provider.CurrentLogLevel);
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
