// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class McpProtocolDefaultsTests
    {
        [TestMethod]
        public void ResolveProtocolVersion_WithoutOverride_UsesLatestDefault()
        {
            IConfiguration config = new ConfigurationBuilder().Build();

            string resolved = McpProtocolDefaults.ResolveProtocolVersion(config);

            Assert.AreEqual("2025-11-25", resolved);
        }

        [DataTestMethod]
        [DataRow("2025-11-25", "2026-01-01", "2025-11-25")]
        [DataRow("2025-11-25", "2025-06-18", "2025-06-18")]
        [DataRow("2025-11-25", "2025-11-25", "2025-11-25")]
        [DataRow("2025-11-25", null, "2025-11-25")]
        [DataRow("a-version", "z-version", "a-version")]
        public void ResolveInitializeResponseProtocolVersion_ReturnsExpectedNegotiatedVersion(
            string supportedProtocolVersion,
            string clientRequestedProtocolVersion,
            string expectedVersion)
        {
            string resolved = McpProtocolDefaults.ResolveInitializeResponseProtocolVersion(
                supportedProtocolVersion,
                clientRequestedProtocolVersion);

            Assert.AreEqual(expectedVersion, resolved);
        }

        [TestMethod]
        public void ShouldUseServerInfoDescription_AtOrAboveThreshold_ReturnsTrue()
        {
            Assert.IsTrue(McpProtocolDefaults.ShouldUseServerInfoDescription("2025-11-25"));
            Assert.IsTrue(McpProtocolDefaults.ShouldUseServerInfoDescription("2025-12-01"));
        }

        [TestMethod]
        public void ShouldUseServerInfoDescription_BelowThreshold_ReturnsFalse()
        {
            Assert.IsFalse(McpProtocolDefaults.ShouldUseServerInfoDescription("2025-06-18"));
        }
    }
}
