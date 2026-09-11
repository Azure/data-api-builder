// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    [TestClass]
    public class McpServerConfigurationTests
    {
        /// <summary>
        /// Verifies server identity and tool capabilities are always configured while blank instructions are omitted.
        /// </summary>
        [DataTestMethod]
        [DataRow(null, null, DisplayName = "Null instructions are omitted")]
        [DataRow("   ", null, DisplayName = "Whitespace instructions are omitted")]
        [DataRow("Use discovered tools only.", "Use discovered tools only.", DisplayName = "Nonblank instructions are preserved")]
        public void ConfigureMcpServer_ConfiguresServerOptions(string? instructions, string? expectedInstructions)
        {
            ServiceCollection services = new();

            IServiceProvider provider = services.ConfigureMcpServer(instructions).BuildServiceProvider();
            McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

            Assert.AreEqual(McpProtocolDefaults.MCP_SERVER_NAME, options.ServerInfo!.Name);
            Assert.AreEqual(McpProtocolDefaults.MCP_SERVER_VERSION, options.ServerInfo.Version);
            Assert.IsNotNull(options.Capabilities);
            Assert.IsNotNull(options.Capabilities.Tools);
            Assert.AreEqual(expectedInstructions, options.ServerInstructions);
        }
    }
}
