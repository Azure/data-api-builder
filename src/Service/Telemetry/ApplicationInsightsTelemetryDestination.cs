// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Validated routing for the product's Application Insights instance. Never reads customer
    /// configuration or falls back to an ambient SDK destination. Does not contact the service.
    /// </summary>
    internal sealed class ApplicationInsightsTelemetryDestination
    {
        private const int MAX_CONNECTION_STRING_LENGTH = 4096;

        private ApplicationInsightsTelemetryDestination(Guid instrumentationKey, Uri ingestionEndpoint)
        {
            InstrumentationKey = instrumentationKey;
            TrackEndpoint = new Uri(ingestionEndpoint, "v2.1/track");
            ConnectionString = $"InstrumentationKey={instrumentationKey:D};IngestionEndpoint={ingestionEndpoint.AbsoluteUri}";
        }

        internal Guid InstrumentationKey { get; }
        internal Uri TrackEndpoint { get; }
        internal string ConnectionString { get; }

        // A record's generated ToString would expose the routing key in diagnostics.
        public override string ToString() => "Application Insights product telemetry destination";

        internal static bool TryParse(string? connectionString, [NotNullWhen(true)] out ApplicationInsightsTelemetryDestination? destination)
        {
            destination = null;
            if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Length > MAX_CONNECTION_STRING_LENGTH || connectionString.Any(char.IsControl))
            {
                return false;
            }

            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            string[] parts = connectionString.Trim().Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0 && i == parts.Length - 1)
                {
                    continue;
                }

                int separator = part.IndexOf('=');
                if (separator <= 0 || separator != part.LastIndexOf('='))
                {
                    return false;
                }

                string key = part[..separator].Trim();
                string value = part[(separator + 1)..].Trim();
                if (value.Length == 0 || !values.TryAdd(key, value) || !IsSupportedSetting(key, value))
                {
                    return false;
                }
            }

            if (!values.TryGetValue("InstrumentationKey", out string? keyValue) ||
                !Guid.TryParseExact(keyValue, "D", out Guid instrumentationKey) || instrumentationKey == Guid.Empty)
            {
                return false;
            }

            values.TryGetValue("EndpointSuffix", out string? suffix);
            values.TryGetValue("Location", out string? location);
            if ((suffix is not null && !IsAzureEndpointSuffix(suffix)) ||
                (location is not null && (suffix is null || location.Length > 63 || !location.All(char.IsAsciiLetterOrDigit))))
            {
                return false;
            }

            if (!values.TryGetValue("IngestionEndpoint", out string? endpoint))
            {
                if (suffix is null)
                {
                    return false;
                }

                string regionPrefix = location is null ? string.Empty : location + ".";
                endpoint = $"https://{regionPrefix}dc.{suffix}/";
            }

            if (!TryParseHttpsOrigin(endpoint, out Uri? ingestionEndpoint))
            {
                return false;
            }

            destination = new(instrumentationKey, ingestionEndpoint);
            return true;
        }

        private static bool IsSupportedSetting(string key, string value) => key.ToUpperInvariant() switch
        {
            "INSTRUMENTATIONKEY" or "INGESTIONENDPOINT" or "ENDPOINTSUFFIX" or "LOCATION" => true,
            // These standard fields may appear in a portal connection string but are not used.
            "LIVEENDPOINT" => TryParseHttpsOrigin(value, out _),
            "APPLICATIONID" => Guid.TryParseExact(value, "D", out Guid id) && id != Guid.Empty,
            "AUTHORIZATION" => string.Equals(value, "ikey", StringComparison.OrdinalIgnoreCase),
            _ => false // No tokens, credentials, audience overrides or arbitrary extensions.
        };

        private static bool IsAzureEndpointSuffix(string suffix) => suffix.ToLowerInvariant() is
            "applicationinsights.azure.com" or "applicationinsights.azure.cn" or "applicationinsights.us";

        private static bool TryParseHttpsOrigin(string value, [NotNullWhen(true)] out Uri? endpoint)
        {
            endpoint = null;
            // Inspect the original path too: Uri normalization must not turn /a/.. into /.
            int schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
            int pathStart = schemeEnd < 0 ? -1 : value.IndexOf('/', schemeEnd + 3);
            if (schemeEnd < 0 || (pathStart >= 0 && pathStart != value.Length - 1) ||
                value.Any(char.IsWhiteSpace) || value.IndexOfAny(['\\', '@', '?', '#', '%']) >= 0 ||
                !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || !uri.IsWellFormedOriginalString() ||
                uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) ||
                uri.HostNameType == UriHostNameType.Unknown || uri.Port <= 0 ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            {
                return false;
            }

            endpoint = uri;
            return true;
        }
    }
}
