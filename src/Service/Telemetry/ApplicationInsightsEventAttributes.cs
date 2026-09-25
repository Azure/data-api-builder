// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Azure.DataApiBuilder.Core.Telemetry.Product;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>Maps the immutable product contract to the SDK's custom-event attributes.</summary>
    internal static class ApplicationInsightsEventAttributes
    {
        internal const string EVENT_NAME_ATTRIBUTE = "microsoft.custom_event.name";
        internal const string CLIENT_IP_ATTRIBUTE = "microsoft.client.ip";
        internal const string SUPPRESSED_IP = "0.0.0.0";
        private const int MAX_PROPERTIES = 128;
        private const int MAX_PROPERTY_NAME_LENGTH = 150;
        private const int MAX_PROPERTY_VALUE_LENGTH = 8192;

        internal static KeyValuePair<string, object?>[] Create(IProductTelemetryEvent record)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (string.IsNullOrWhiteSpace(record.Name) || record.Name.Length > 512 || record.Properties.Count > MAX_PROPERTIES)
            {
                throw new ArgumentException("Product telemetry event exceeds the ingestion contract.", nameof(record));
            }

            Dictionary<string, object?> attributes = new(StringComparer.Ordinal);
            foreach ((string key, string value) in record.Properties)
            {
                if (string.IsNullOrEmpty(key) || key.Length > MAX_PROPERTY_NAME_LENGTH ||
                    value is null || value.Length > MAX_PROPERTY_VALUE_LENGTH)
                {
                    throw new ArgumentException("Product telemetry property exceeds the ingestion contract.", nameof(record));
                }

                // Core owns the allowlist. Do not interpret supplied diagnostic attributes as
                // SDK instructions or let them replace the event's immutable metadata.
                if (key.StartsWith("microsoft.", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("ai.", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("enduser.", StringComparison.OrdinalIgnoreCase) ||
                    key is "user_agent.original" or "{OriginalFormat}" or "CategoryName" or "EventId" or "EventName" or "dab_config_epoch")
                {
                    continue;
                }

                attributes.Add(key, value);
            }

            attributes[EVENT_NAME_ATTRIBUTE] = record.Name;
            // Missing IP allows ingestion to geolocate the connection before masking it.
            // The SDK maps this constant, never an observed address, to ai.location.ip.
            attributes[CLIENT_IP_ATTRIBUTE] = SUPPRESSED_IP;
            attributes["dab_event_id"] = record.EventId.ToString("D");
            attributes["dab_process_session_id"] = record.SessionId.ToString("D");
            attributes["dab_sequence"] = record.Sequence.ToString(CultureInfo.InvariantCulture);
            attributes["dab_occurred_at"] = record.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            if (record is EngineTelemetryEvent engineRecord)
            {
                attributes["dab_config_epoch"] = engineRecord.ConfigurationEpoch.ToString(CultureInfo.InvariantCulture);
            }

            attributes["dab_schema_version"] = "1";
            attributes["dab_is_synthetic"] = record.IsSynthetic ? "true" : "false";
            return attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        }
    }
}
