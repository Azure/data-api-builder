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
            Assert.AreEqual("mcp", fields["api"]);
            Assert.AreEqual("3", fields["count"]);
            CollectionAssert.AreEqual(attributes, ApplicationInsightsEventAttributes.Create(record));
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
            4, "dab.engine.usage_summary", ImmutableDictionary<string, string>.Empty.Add("api", "mcp").Add("count", "3"));
    }
}
