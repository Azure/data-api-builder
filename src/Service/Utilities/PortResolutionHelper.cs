// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Azure.DataApiBuilder.Service.Utilities
{
    /// <summary>
    /// Provides methods to resolve the internal port for the application based on environment variables.
    /// </summary>
    public static class PortResolutionHelper
    {
        /// <summary>
        /// Resolves the internal port used by the application based on environment variables and URL bindings.
        /// </summary>
        /// <remarks>This method determines the port by checking the <c>ASPNETCORE_URLS</c> environment
        /// variable for URL bindings. If a valid port is found in the URLs, it is returned. If no port is specified,
        /// the method checks the <c>DEFAULT_PORT</c> environment variable for a fallback port. If neither is set, the
        /// default port of 5000 is returned.</remarks>
        /// <returns>The resolved port number. Returns the port specified in <c>ASPNETCORE_URLS</c>, or the fallback port from
        /// <c>DEFAULT_PORT</c>, or 5000 if no port is configured.</returns>
        public static int ResolveInternalPort()
        {
            string? urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
            int? httpsPort = null;

            if (!string.IsNullOrWhiteSpace(urls))
            {
                string[] parts = urls.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string part in parts)
                {
                    string trimmedPart = part.Trim();

                    // Try to parse as a valid URI first
                    if (Uri.TryCreate(trimmedPart, UriKind.Absolute, out Uri? uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        if (uri.Scheme == Uri.UriSchemeHttp)
                        {
                            return uri.Port;
                        }
                        else if (uri.Scheme == Uri.UriSchemeHttps)
                        {
                            httpsPort ??= uri.Port;
                        }

                        continue;
                    }

                    // Handle known wildcard patterns (http/https with + or * as host)
                    // Example: http://+:1234 or http://*:1234 or https://+:1234 or https://*:1234
                    if (trimmedPart.StartsWith("http://+:", StringComparison.OrdinalIgnoreCase) ||
                        trimmedPart.StartsWith("http://*:", StringComparison.OrdinalIgnoreCase))
                    {
                        string portString = trimmedPart.Substring(trimmedPart.LastIndexOf(':') + 1);

                        if (int.TryParse(portString, out int port) && port > 0)
                        {
                            return port;
                        }

                        continue;
                    }

                    if (trimmedPart.StartsWith("https://+:", StringComparison.OrdinalIgnoreCase) ||
                        trimmedPart.StartsWith("https://*:", StringComparison.OrdinalIgnoreCase))
                    {
                        string portString = trimmedPart.Substring(trimmedPart.LastIndexOf(':') + 1);

                        if (int.TryParse(portString, out int port) && port > 0)
                        {
                            httpsPort ??= port;
                        }

                        continue;
                    }
                }
            }

            // If no HTTP, fallback to HTTPS port if present
            if (httpsPort.HasValue)
            {
                return httpsPort.Value;
            }

            // Check ASPNETCORE_HTTP_PORTS if ASPNETCORE_URLS is not set
            string? httpPorts = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");

            if (!string.IsNullOrWhiteSpace(httpPorts))
            {
                string[] portParts = httpPorts.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string portPart in portParts)
                {
                    string trimmedPort = portPart.Trim();

                    if (int.TryParse(trimmedPort, out int port) && port > 0)
                    {
                        return port;
                    }
                }
            }

            // Configurable fallback port
            string? defaultPortEnv = Environment.GetEnvironmentVariable("DEFAULT_PORT");

            if (int.TryParse(defaultPortEnv, out int defaultPort) && defaultPort > 0)
            {
                return defaultPort;
            }

            // Default Kestrel port if not specified.
            return 5000;
        }
    }
}
