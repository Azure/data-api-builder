// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class McpServerConfigurationTests
    {
        [TestMethod]
        public void ConfigureMcpServer_HttpDoesNotAdvertiseToolListChanges()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.ConfigureMcpServer(instructions: null);

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            McpServerOptions options = serviceProvider
                .GetRequiredService<IOptions<McpServerOptions>>()
                .Value;

            Assert.IsNotNull(options.Capabilities);
            Assert.IsNotNull(options.Capabilities.Tools);
            Assert.IsFalse(
                options.Capabilities.Tools.ListChanged,
                "HTTP must not promise tool-list notifications until session broadcast is implemented.");
        }
    }
}
