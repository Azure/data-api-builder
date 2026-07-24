// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Middleware that protects the MCP Streamable HTTP transport against DNS rebinding attacks.
    ///
    /// The MCP endpoint is browser-reachable. A malicious web page can keep its attacker origin
    /// while its host name is rebound (via DNS) to a loopback or private DAB address and then send
    /// MCP JSON-RPC requests to the configured MCP path. Because DAB holds the backend database
    /// connection, such a session could invoke the MCP tool surface using DAB's configured authority.
    ///
    /// To prevent this, every request targeting the MCP path is validated against a set of trusted
    /// host names before it reaches the MCP transport:
    /// - The incoming <c>Host</c> header must resolve to a trusted host name.
    /// - When present, the <c>Origin</c> header's host must also resolve to a trusted host name.
    ///
    /// Loopback host names (localhost, 127.0.0.1, ::1) are always trusted so that local MCP clients
    /// continue to function. Operators can add additional trusted hosts via the
    /// <c>runtime.mcp.allowed-hosts</c> configuration. A single entry of <c>"*"</c> disables the
    /// validation for deployments that terminate host validation upstream (not recommended).
    /// </summary>
    public class McpDnsRebindingProtectionMiddleware
    {
        private readonly RequestDelegate _nextMiddleware;

        /// <summary>
        /// Wildcard value which, when present in the allowed-hosts list, disables Host/Origin validation.
        /// </summary>
        private const string ALLOW_ALL_HOSTS = "*";

        /// <summary>
        /// Loopback host names that are always trusted, allowing local MCP clients to connect.
        /// </summary>
        private static readonly HashSet<string> _loopbackHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "localhost",
            "127.0.0.1",
            "::1"
        };

        public McpDnsRebindingProtectionMiddleware(RequestDelegate next)
        {
            _nextMiddleware = next;
        }

        /// <summary>
        /// Validates the Host and Origin headers for requests targeting the MCP endpoint before
        /// allowing them to proceed through the pipeline.
        /// </summary>
        public async Task InvokeAsync(HttpContext httpContext, RuntimeConfigProvider runtimeConfigProvider)
        {
            if (!runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                await _nextMiddleware(httpContext);
                return;
            }

            McpRuntimeOptions mcpOptions = runtimeConfig.Runtime?.Mcp ?? new McpRuntimeOptions();

            // Only guard requests that are handled by the MCP endpoint.
            if (!mcpOptions.Enabled)
            {
                await _nextMiddleware(httpContext);
                return;
            }

            string mcpPath = mcpOptions.Path ?? McpRuntimeOptions.DEFAULT_PATH;
            if (!httpContext.Request.Path.StartsWithSegments(mcpPath))
            {
                await _nextMiddleware(httpContext);
                return;
            }

            if (!IsRequestFromTrustedHost(httpContext.Request, mcpOptions.AllowedHosts, out string? rejectionReason))
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response.WriteAsync(
                    "{\"error\":\"Forbidden\",\"message\":\"" + rejectionReason + "\"}");
                return;
            }

            await _nextMiddleware(httpContext);
        }

        /// <summary>
        /// Determines whether a request targeting the MCP endpoint originates from a trusted host,
        /// validating both the Host and (when present) Origin headers against the trusted host set.
        /// </summary>
        /// <param name="request">The incoming HTTP request.</param>
        /// <param name="configuredAllowedHosts">Additional trusted hosts from configuration.</param>
        /// <param name="rejectionReason">A description of why the request was rejected, when applicable.</param>
        /// <returns>True when the request is allowed; otherwise false.</returns>
        internal static bool IsRequestFromTrustedHost(
            HttpRequest request,
            IReadOnlyList<string>? configuredAllowedHosts,
            out string? rejectionReason)
        {
            rejectionReason = null;

            HashSet<string> trustedHosts = BuildTrustedHostSet(configuredAllowedHosts, out bool allowAllHosts);
            if (allowAllHosts)
            {
                return true;
            }

            // Validate the Host header. A browser always sends a Host header, and a DNS rebinding
            // request carries the attacker-controlled host name rather than a trusted host.
            string hostHeaderValue = NormalizeHost(request.Host.Host);
            if (string.IsNullOrEmpty(hostHeaderValue) || !trustedHosts.Contains(hostHeaderValue))
            {
                rejectionReason =
                    "The request Host header is not in the list of trusted hosts allowed to reach the MCP endpoint. " +
                    "Configure runtime.mcp.allowed-hosts to permit non-loopback hosts.";
                return false;
            }

            // Validate the Origin header when present (browser-initiated cross-origin requests).
            if (request.Headers.TryGetValue("Origin", out Microsoft.Extensions.Primitives.StringValues originValues))
            {
                string? originValue = originValues.ToString();
                if (!string.IsNullOrEmpty(originValue))
                {
                    if (!TryGetOriginHost(originValue, out string originHost)
                        || !trustedHosts.Contains(originHost))
                    {
                        rejectionReason =
                            "The request Origin header is not in the list of trusted hosts allowed to reach the MCP endpoint. " +
                            "Configure runtime.mcp.allowed-hosts to permit non-loopback origins.";
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Builds the set of trusted host names, always including loopback hosts and any
        /// configured allowed hosts. Detects the wildcard opt-out value.
        /// </summary>
        private static HashSet<string> BuildTrustedHostSet(IReadOnlyList<string>? configuredAllowedHosts, out bool allowAllHosts)
        {
            allowAllHosts = false;
            HashSet<string> trustedHosts = new(_loopbackHosts, StringComparer.OrdinalIgnoreCase);

            if (configuredAllowedHosts is not null)
            {
                foreach (string configuredHost in configuredAllowedHosts)
                {
                    if (string.IsNullOrWhiteSpace(configuredHost))
                    {
                        continue;
                    }

                    if (string.Equals(configuredHost.Trim(), ALLOW_ALL_HOSTS, StringComparison.Ordinal))
                    {
                        allowAllHosts = true;
                        continue;
                    }

                    trustedHosts.Add(NormalizeHost(configuredHost));
                }
            }

            return trustedHosts;
        }

        /// <summary>
        /// Extracts the host component from an Origin header value.
        /// </summary>
        private static bool TryGetOriginHost(string originValue, out string originHost)
        {
            originHost = string.Empty;

            // The literal string "null" is sent for opaque origins (e.g., sandboxed iframes,
            // file:// pages). Treat it as untrusted.
            if (string.Equals(originValue.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (Uri.TryCreate(originValue, UriKind.Absolute, out Uri? originUri))
            {
                originHost = NormalizeHost(originUri.Host);
                return !string.IsNullOrEmpty(originHost);
            }

            return false;
        }

        /// <summary>
        /// Normalizes a host name for comparison by trimming surrounding whitespace and IPv6
        /// bracket delimiters. Comparisons are performed case-insensitively.
        /// </summary>
        private static string NormalizeHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return string.Empty;
            }

            return host.Trim().Trim('[', ']');
        }
    }
}
