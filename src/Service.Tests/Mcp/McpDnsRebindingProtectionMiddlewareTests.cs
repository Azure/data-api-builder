// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Unit tests for <see cref="McpDnsRebindingProtectionMiddleware"/> which protects the
    /// browser-reachable MCP Streamable HTTP transport against DNS rebinding attacks by
    /// validating the Host and Origin headers before the request reaches the MCP endpoint.
    /// </summary>
    [TestClass]
    public class McpDnsRebindingProtectionMiddlewareTests
    {
        private const string CUSTOM_CONFIG = "mcp-dns-rebinding-config.json";
        private const string MCP_PATH = "/mcp";

        #region IsRequestFromTrustedHost (pure logic)

        /// <summary>
        /// Loopback hosts are always trusted so that local MCP clients continue to work
        /// without additional configuration.
        /// </summary>
        [DataTestMethod]
        [DataRow("localhost", DisplayName = "localhost host")]
        [DataRow("127.0.0.1", DisplayName = "IPv4 loopback host")]
        [DataRow("::1", DisplayName = "IPv6 loopback host")]
        public void IsRequestFromTrustedHost_LoopbackHost_IsAllowed(string host)
        {
            HttpRequest request = BuildRequest(host: host);

            bool allowed = McpDnsRebindingProtectionMiddleware.IsRequestFromTrustedHost(
                request, configuredAllowedHosts: null, out string reason);

            Assert.IsTrue(allowed, $"Loopback host '{host}' should be trusted. Reason: {reason}");
            Assert.IsNull(reason);
        }

        /// <summary>
        /// A rebound request carries the attacker's host name in the Host header, which is not
        /// a trusted host and must be rejected. This is the core DNS rebinding defense.
        /// </summary>
        [TestMethod]
        public void IsRequestFromTrustedHost_AttackerHost_IsRejected()
        {
            HttpRequest request = BuildRequest(host: "attacker.com");

            bool allowed = McpDnsRebindingProtectionMiddleware.IsRequestFromTrustedHost(
                request, configuredAllowedHosts: null, out string reason);

            Assert.IsFalse(allowed, "Untrusted attacker host should be rejected.");
            StringAssert.Contains(reason, "Host header");
        }

        /// <summary>
        /// The port component of the Host header is ignored; only the host name is validated.
        /// </summary>
        [TestMethod]
        public void IsRequestFromTrustedHost_LoopbackWithPort_IsAllowed()
        {
            HttpRequest request = BuildRequest(host: "localhost", port: 8087);

            bool allowed = McpDnsRebindingProtectionMiddleware.IsRequestFromTrustedHost(
                request, configuredAllowedHosts: null, out string reason);

            Assert.IsTrue(allowed, $"Loopback host with a port should be trusted. Reason: {reason}");
        }

        /// <summary>
        /// A configured allowed host is trusted for non-loopback deployments.
        /// </summary>
        [TestMethod]
        public void IsRequestFromTrustedHost_ConfiguredHost_IsAllowed()
        {
            HttpRequest request = BuildRequest(host: "dab.internal");

            bool allowed = McpDnsRebindingProtectionMiddleware.IsRequestFromTrustedHost(
                request, configuredAllowedHosts: new List<string> { "dab.internal" }, out string reason);

            Assert.IsTrue(allowed, $"Configured host should be trusted. Reason: {reason}");
        }

        /// <summary>
        /// Host name comparison is case-insensitive.
        /// </summary>
        [TestMethod]
        public void IsRequestFromTrustedHost_ConfiguredHostDifferentCase_IsAllowed()
        {
            HttpRequest request = BuildRequest(host: "DAB.Internal");

            bool allowed = McpDnsRebindingProtectionMiddleware.IsRequestFromTrustedHost(
                request, configuredAllowedHosts: new List<string> { "dab.internal" }, out _);

            Assert.IsTrue(allowed, "Host comparison should be case-insensitive.");
        }

        /// <summary>
        /// The wildcard entry disables Host/Origin validation for deployments that terminate
        /// host validation upstream.
        /// </summary>
        [TestMethod]
        public void IsRequestFromTrustedHost_WildcardAllowedHost_AllowsAnyHost()
        {
            HttpRequest request = BuildRequest(host: "attacker.com", origin: "http://attacker.com");

            bool allowed = McpDnsRebindingProtectionMiddleware.IsRequestFromTrustedHost(
                request, configuredAllowedHosts: new List<string> { "*" }, out string reason);

            Assert.IsTrue(allowed, "Wildcard should disable Host/Origin validation.");
            Assert.IsNull(reason);
        }

        /// <summary>
        /// When a request has a trusted Host header but an untrusted Origin header (the classic
        /// DNS rebinding cross-origin scenario), the request is rejected.
        /// </summary>
        [TestMethod]
        public void IsRequestFromTrustedHost_TrustedHostUntrustedOrigin_IsRejected()
        {
            HttpRequest request = BuildRequest(host: "localhost", origin: "http://attacker.com");

            bool allowed = McpDnsRebindingProtectionMiddleware.IsRequestFromTrustedHost(
                request, configuredAllowedHosts: null, out string reason);

            Assert.IsFalse(allowed, "Untrusted Origin should be rejected even with a trusted Host.");
            StringAssert.Contains(reason, "Origin header");
        }

        /// <summary>
        /// A trusted Host together with a trusted (loopback) Origin is allowed.
        /// </summary>
        [TestMethod]
        public void IsRequestFromTrustedHost_TrustedHostTrustedOrigin_IsAllowed()
        {
            HttpRequest request = BuildRequest(host: "localhost", port: 5000, origin: "http://localhost:5000");

            bool allowed = McpDnsRebindingProtectionMiddleware.IsRequestFromTrustedHost(
                request, configuredAllowedHosts: null, out string reason);

            Assert.IsTrue(allowed, $"Trusted Host and Origin should be allowed. Reason: {reason}");
        }

        /// <summary>
        /// An opaque Origin (the literal string "null" sent by sandboxed iframes and file:// pages)
        /// is treated as untrusted.
        /// </summary>
        [TestMethod]
        public void IsRequestFromTrustedHost_NullOrigin_IsRejected()
        {
            HttpRequest request = BuildRequest(host: "localhost", origin: "null");

            bool allowed = McpDnsRebindingProtectionMiddleware.IsRequestFromTrustedHost(
                request, configuredAllowedHosts: null, out string reason);

            Assert.IsFalse(allowed, "Opaque 'null' Origin should be rejected.");
            StringAssert.Contains(reason, "Origin header");
        }

        /// <summary>
        /// Requests without an Origin header (non-browser MCP clients) are validated on the Host
        /// header alone.
        /// </summary>
        [TestMethod]
        public void IsRequestFromTrustedHost_NoOriginHeaderTrustedHost_IsAllowed()
        {
            HttpRequest request = BuildRequest(host: "127.0.0.1");

            bool allowed = McpDnsRebindingProtectionMiddleware.IsRequestFromTrustedHost(
                request, configuredAllowedHosts: null, out _);

            Assert.IsTrue(allowed, "A request with a trusted Host and no Origin should be allowed.");
        }

        /// <summary>
        /// An empty Host header is rejected.
        /// </summary>
        [TestMethod]
        public void IsRequestFromTrustedHost_EmptyHost_IsRejected()
        {
            HttpRequest request = new DefaultHttpContext().Request;

            bool allowed = McpDnsRebindingProtectionMiddleware.IsRequestFromTrustedHost(
                request, configuredAllowedHosts: null, out string reason);

            Assert.IsFalse(allowed, "An empty Host header should be rejected.");
            StringAssert.Contains(reason, "Host header");
        }

        #endregion

        #region InvokeAsync (pipeline behavior)

        /// <summary>
        /// Requests that do not target the MCP path bypass the DNS rebinding validation.
        /// </summary>
        [TestMethod]
        public async Task InvokeAsync_NonMcpPath_CallsNext()
        {
            RuntimeConfigProvider provider = BuildProvider(BuildConfig());
            (McpDnsRebindingProtectionMiddleware middleware, NextTracker next) = BuildMiddleware();

            HttpContext context = BuildContext(path: "/api/Book", host: "attacker.com");
            await middleware.InvokeAsync(context, provider);

            Assert.IsTrue(next.WasCalled, "Non-MCP path requests should pass through to the next middleware.");
            Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        /// <summary>
        /// When MCP is disabled, no validation is applied even for the MCP path.
        /// </summary>
        [TestMethod]
        public async Task InvokeAsync_McpDisabled_CallsNext()
        {
            RuntimeConfigProvider provider = BuildProvider(BuildConfig(mcpEnabled: false));
            (McpDnsRebindingProtectionMiddleware middleware, NextTracker next) = BuildMiddleware();

            HttpContext context = BuildContext(path: MCP_PATH, host: "attacker.com");
            await middleware.InvokeAsync(context, provider);

            Assert.IsTrue(next.WasCalled, "When MCP is disabled the request should pass through.");
        }

        /// <summary>
        /// A DNS rebinding request to the MCP path with an untrusted Host is rejected with 403
        /// and does not reach the MCP endpoint.
        /// </summary>
        [TestMethod]
        public async Task InvokeAsync_McpPathUntrustedHost_ReturnsForbidden()
        {
            RuntimeConfigProvider provider = BuildProvider(BuildConfig());
            (McpDnsRebindingProtectionMiddleware middleware, NextTracker next) = BuildMiddleware();

            HttpContext context = BuildContext(path: MCP_PATH, host: "attacker.com", origin: "http://attacker.com");
            await middleware.InvokeAsync(context, provider);

            Assert.IsFalse(next.WasCalled, "The MCP endpoint must not be reached for an untrusted host.");
            Assert.AreEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        }

        /// <summary>
        /// A request to the MCP path from a trusted loopback host is allowed to proceed.
        /// </summary>
        [TestMethod]
        public async Task InvokeAsync_McpPathLoopbackHost_CallsNext()
        {
            RuntimeConfigProvider provider = BuildProvider(BuildConfig());
            (McpDnsRebindingProtectionMiddleware middleware, NextTracker next) = BuildMiddleware();

            HttpContext context = BuildContext(path: MCP_PATH, host: "localhost", port: 5000);
            await middleware.InvokeAsync(context, provider);

            Assert.IsTrue(next.WasCalled, "A trusted loopback MCP request should pass through.");
        }

        /// <summary>
        /// A request to the MCP path from a configured trusted host is allowed to proceed.
        /// </summary>
        [TestMethod]
        public async Task InvokeAsync_McpPathConfiguredHost_CallsNext()
        {
            RuntimeConfigProvider provider = BuildProvider(
                BuildConfig(allowedHosts: new List<string> { "dab.contoso.com" }));
            (McpDnsRebindingProtectionMiddleware middleware, NextTracker next) = BuildMiddleware();

            HttpContext context = BuildContext(path: MCP_PATH, host: "dab.contoso.com");
            await middleware.InvokeAsync(context, provider);

            Assert.IsTrue(next.WasCalled, "A configured trusted host MCP request should pass through.");
        }

        #endregion

        #region Helpers

        private static HttpRequest BuildRequest(string host, int? port = null, string origin = null)
        {
            DefaultHttpContext context = new();
            context.Request.Host = port is null ? new HostString(host) : new HostString(host, port.Value);
            if (origin is not null)
            {
                context.Request.Headers["Origin"] = origin;
            }

            return context.Request;
        }

        private static HttpContext BuildContext(string path, string host, int? port = null, string origin = null)
        {
            DefaultHttpContext context = new();
            context.Request.Path = path;
            context.Request.Host = port is null ? new HostString(host) : new HostString(host, port.Value);
            if (origin is not null)
            {
                context.Request.Headers["Origin"] = origin;
            }

            return context;
        }

        private static (McpDnsRebindingProtectionMiddleware, NextTracker) BuildMiddleware()
        {
            NextTracker tracker = new();
            McpDnsRebindingProtectionMiddleware middleware = new(tracker.InvokeAsync);
            return (middleware, tracker);
        }

        private static RuntimeConfig BuildConfig(
            bool mcpEnabled = true,
            List<string> allowedHosts = null)
        {
            DataSource dataSource = new(
                DatabaseType: DatabaseType.MSSQL,
                ConnectionString: "Server=test;Database=test;",
                Options: null);

            McpRuntimeOptions mcpOptions = new(
                Enabled: mcpEnabled,
                Path: MCP_PATH,
                DmlTools: null,
                Description: null,
                AllowedHosts: allowedHosts);

            RuntimeOptions runtimeOptions = new(
                Rest: null,
                GraphQL: null,
                Host: null,
                Mcp: mcpOptions);

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: dataSource,
                Runtime: runtimeOptions,
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
        }

        private static RuntimeConfigProvider BuildProvider(RuntimeConfig config)
        {
            MockFileSystem fileSystem = new();
            fileSystem.AddFile(CUSTOM_CONFIG, new MockFileData(config.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            loader.UpdateConfigFilePath(CUSTOM_CONFIG);
            return new RuntimeConfigProvider(loader);
        }

        /// <summary>
        /// Test double for the next <see cref="RequestDelegate"/> in the pipeline that records
        /// whether it was invoked.
        /// </summary>
        private sealed class NextTracker
        {
            public bool WasCalled { get; private set; }

            public Task InvokeAsync(HttpContext context)
            {
                _ = context;
                WasCalled = true;
                return Task.CompletedTask;
            }
        }

        #endregion
    }
}
