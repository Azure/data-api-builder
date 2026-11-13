// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class ApplicationInsightsEventAttributesTests
    {
        [TestMethod]
        public void CustomEventAttributesPreserveTheVersionedContract()
        {
            EngineTelemetryEvent record = CreateEvent();
            KeyValuePair<string, object?>[] attributes = ApplicationInsightsEventAttributes.Create(record);
            Dictionary<string, object?> fields = attributes.ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.AreEqual(11, fields.Count);
            Assert.AreEqual(record.Name, fields[ApplicationInsightsEventAttributes.EVENT_NAME_ATTRIBUTE]);
            Assert.AreEqual("0.0.0.0", fields[ApplicationInsightsEventAttributes.CLIENT_IP_ATTRIBUTE]);
            Assert.AreEqual(record.EventId.ToString("D"), fields["dab_event_id"]);
            Assert.AreEqual(record.SessionId.ToString("D"), fields["dab_process_session_id"]);
            Assert.AreEqual("17", fields["dab_sequence"]);
            Assert.AreEqual("4", fields["dab_config_epoch"]);
            Assert.AreEqual("1", fields["dab_schema_version"]);
            Assert.AreEqual("true", fields["dab_is_synthetic"]);
            Assert.AreEqual(record.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), fields["dab_occurred_at"]);
            Assert.AreEqual("mcp", fields["dab_api"]);
            Assert.AreEqual("3", fields["dab_count"]);
            CollectionAssert.AreEqual(attributes, ApplicationInsightsEventAttributes.Create(record));
        }

        [DataTestMethod]
        [DataRow("dab/engine.usage_summary")]
        [DataRow("dab/cli.command_completed")]
        public void SharedConversionPrefixesKeysOnceWithoutChangingValues(string name)
        {
            const string json = "{\"runtime.rest.effective\":\"enabled\",\"option_config\":true,\"items\":[1,2]}";
            EngineTelemetryEvent record = CreateEvent() with
            {
                Name = name,
                Properties = CreateEvent().Properties
                    .Add("runtime.rest.effective", "enabled")
                    .Add("option_config", json)
                    .Add("dab_api_id", "f6bb4a71-742a-445b-bca0-d4aa56659c28")
                    .Add("dab_api_id_stability", "ephemeral")
                    .Add("dab_version", "1.2.3")
            };
            ImmutableDictionary<string, string> original = record.Properties;
            Dictionary<string, object?> fields = ApplicationInsightsEventAttributes.Create(record)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            foreach ((string key, string value) in original)
            {
                string expectedKey = key.StartsWith("dab_", StringComparison.Ordinal) ? key : "dab_" + key;
                Assert.AreEqual(value, fields[expectedKey], expectedKey);
            }

            Assert.AreEqual(name, fields[ApplicationInsightsEventAttributes.EVENT_NAME_ATTRIBUTE]);
            Assert.AreEqual(json, fields["dab_option_config"], "JSON property names and values are not rewritten.");
            Assert.IsFalse(fields.Keys.Any(key => key.StartsWith("dab_dab_", StringComparison.Ordinal)));
            Assert.AreSame(original, record.Properties, "The source contract remains immutable and unprefixed.");
            Assert.AreEqual("enabled", record.Properties["runtime.rest.effective"]);
        }

        [DataTestMethod]
        [DataRow(false, 146, true)]
        [DataRow(false, 147, false)]
        [DataRow(true, 150, true)]
        [DataRow(true, 151, false)]
        public void PropertyLengthLimitAppliesAfterPrefixing(bool alreadyPrefixed, int inputLength, bool accepted)
        {
            string key = alreadyPrefixed ? "dab_" + new string('x', inputLength - 4) : new string('x', inputLength);
            EngineTelemetryEvent record = CreateEvent() with { Properties = ImmutableDictionary<string, string>.Empty.Add(key, "value") };
            if (!accepted)
            {
                Assert.ThrowsException<ArgumentException>(() => ApplicationInsightsEventAttributes.Create(record));
                return;
            }

            string expectedKey = alreadyPrefixed ? key : "dab_" + key;
            KeyValuePair<string, object?> property = ApplicationInsightsEventAttributes.Create(record).Single(pair => pair.Key == expectedKey);
            Assert.AreEqual(150, property.Key.Length);
            Assert.AreEqual("value", property.Value);
        }

        [DataTestMethod]
        [DataRow("count")]
        [DataRow("runtime.rest.effective")]
        [DataRow("option_config")]
        [DataRow("event_id")]
        public void CollidingPrefixedPropertiesAreRejectedWithoutOverwritingValues(string key)
        {
            EngineTelemetryEvent record = CreateEvent() with
            {
                Properties = ImmutableDictionary<string, string>.Empty.Add(key, "original").Add("dab_" + key, "prefixed")
            };

            Assert.ThrowsException<ArgumentException>(() => ApplicationInsightsEventAttributes.Create(record));
            Assert.AreEqual("original", record.Properties[key]);
            Assert.AreEqual("prefixed", record.Properties["dab_" + key]);
        }

        [TestMethod]
        public void SuppliedSdkInstructionsCannotReplaceEnvelopeOrAddContext()
        {
            const string sentinel = "PRIVATE_CONTEXT_VALUE";
            EngineTelemetryEvent record = CreateEvent() with
            {
                Properties = CreateEvent().Properties
                    .Add("microsoft.client.ip", sentinel).Add("ai.location.ip", sentinel)
                    .Add("microsoft.custom_event.name", sentinel).Add("ai.cloud.role", sentinel)
                    .Add("enduser.id", sentinel).Add("user_agent.original", sentinel)
                    .Add("{OriginalFormat}", sentinel).Add("CategoryName", sentinel)
                    .Add("dab_event_id", sentinel).Add("dab_is_synthetic", sentinel)
            };
            Assert.IsFalse(ApplicationInsightsEventAttributes.Create(record).Any(pair => Equals(pair.Value, sentinel)));
            Assert.AreEqual(sentinel, record.Properties["dab_event_id"], "The immutable source must not be modified.");
        }

        [DataTestMethod]
        [DataRow("name")]
        [DataRow("key")]
        [DataRow("value")]
        [DataRow("count")]
        public void InvalidOrOversizedEventIsRejectedInsteadOfTruncated(string oversized)
        {
            EngineTelemetryEvent record = CreateEvent();
            record = oversized switch
            {
                "name" => record with { Name = new string('x', 513) },
                "key" => record with { Properties = record.Properties.Add(new string('x', 151), "value") },
                "value" => record with { Properties = record.Properties.Add("large", new string('x', 8193)) },
                _ => record with { Properties = Enumerable.Range(0, 129).ToImmutableDictionary(i => "field" + i, _ => "value") }
            };
            Assert.ThrowsException<ArgumentException>(() => ApplicationInsightsEventAttributes.Create(record));
        }

        [DataTestMethod]
        [DataRow("not json")]
        [DataRow("[]")]
        [DataRow("{}")]
        [DataRow("{\"tags\":{}}")]
        [DataRow("{\"tags\":{\"ai.location.ip\":null}}")]
        [DataRow("{\"tags\":{\"ai.location.ip\":\"192.0.2.1\"}}")]
        [DataRow("{\"tags\":{\"ai.location.ip\":\"0.0.0.0\",\"ai.location.ip\":\"0.0.0.0\"}}")]
        [DataRow("{\"tags\":{\"ai.location.ip\":\"0.0.0.0\",\"ai.cloud.roleInstance\":\"PRIVATE_HOST\"}}")]
        [DataRow("{\"tags\":{\"ai.location.ip\":\"0.0.0.0\",\"ai.user.id\":\"PRIVATE_USER\"}}")]
        [DataRow("{\"tags\":{\"ai.location.ip\":\"0.0.0.0\",\"ai.operation.id\":\"PRIVATE_TRACE\"}}")]
        public void UnexpectedSdkContextOrMissingIpSuppressionIsRejected(string payload)
        {
            Assert.IsFalse(EngineTelemetrySdkTransportHandler.HasOnlyApprovedEnvelopeTags(payload));
        }

        [TestMethod]
        public void SdkVersionAndNullResourceTagsArePermittedWithSuppressedIp()
        {
            Assert.IsTrue(EngineTelemetrySdkTransportHandler.HasOnlyApprovedEnvelopeTags("""
                {"tags":{"ai.location.ip":"0.0.0.0","ai.internal.sdkVersion":"dotnet10:otel1.18:ext1.9",
                "ai.cloud.role":null,"ai.cloud.roleInstance":null,"ai.application.ver":null}}
                """));
        }

        private static EngineTelemetryEvent CreateEvent() => new(
            Guid.NewGuid(), Guid.NewGuid(), 17,
            new DateTimeOffset(2026, 9, 21, 10, 11, 12, TimeSpan.FromMinutes(330)).AddTicks(3456789),
            4, "dab/engine.usage_summary", ImmutableDictionary<string, string>.Empty.Add("api", "mcp").Add("count", "3"));
    }
}
