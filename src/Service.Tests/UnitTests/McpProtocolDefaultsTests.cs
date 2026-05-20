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

        [TestMethod]
        public void ResolveInitializeResponseProtocolVersion_ClientRequestsNewerVersion_ReturnsServerSupportedVersion()
        {
            string resolved = McpProtocolDefaults.ResolveInitializeResponseProtocolVersion(
                supportedProtocolVersion: "2025-11-25",
                clientRequestedProtocolVersion: "2026-01-01");

            Assert.AreEqual("2025-11-25", resolved);
        }

        [TestMethod]
        public void ResolveInitializeResponseProtocolVersion_ClientRequestsOlderVersion_ReturnsClientVersion()
        {
            string resolved = McpProtocolDefaults.ResolveInitializeResponseProtocolVersion(
                supportedProtocolVersion: "2025-11-25",
                clientRequestedProtocolVersion: "2025-06-18");

            Assert.AreEqual("2025-06-18", resolved);
        }

        [TestMethod]
        public void ResolveInitializeResponseProtocolVersion_WithoutClientVersion_ReturnsSupportedVersion()
        {
            string resolved = McpProtocolDefaults.ResolveInitializeResponseProtocolVersion(
                supportedProtocolVersion: "2025-11-25",
                clientRequestedProtocolVersion: null);

            Assert.AreEqual("2025-11-25", resolved);
        }

        [TestMethod]
        public void ResolveInitializeResponseProtocolVersion_NonDateVersionFormat_UsesOrdinalFallbackComparison()
        {
            string resolved = McpProtocolDefaults.ResolveInitializeResponseProtocolVersion(
                supportedProtocolVersion: "a-version",
                clientRequestedProtocolVersion: "z-version");

            Assert.AreEqual("a-version", resolved);
        }
    }
}
