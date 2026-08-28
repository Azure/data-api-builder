// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.HealthCheck;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class EntityHealthOptionsConverterTests
    {
        private static JsonSerializerOptions Options => RuntimeConfigLoader.GetSerializationOptions();

        [TestMethod]
        public void Deserialize_Null_UsesDefaults()
        {
            EntityHealthOptionsConvertorFactory factory = new();
            JsonConverter<EntityHealthCheckConfig> converter =
                (JsonConverter<EntityHealthCheckConfig>)factory.CreateConverter(typeof(EntityHealthCheckConfig), Options)!;
            Utf8JsonReader reader = new("null"u8);
            Assert.IsTrue(reader.Read());

            EntityHealthCheckConfig? result = converter.Read(ref reader, typeof(EntityHealthCheckConfig), Options);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Enabled);
            Assert.AreEqual(HealthCheckConstants.DEFAULT_FIRST_VALUE, result.First);
            Assert.AreEqual(HealthCheckConstants.DEFAULT_THRESHOLD_RESPONSE_TIME_MS, result.ThresholdMs);
        }

        [TestMethod]
        public void Deserialize_AllProperties_PreservesValuesAndPresence()
        {
            const string json = """
                {
                  "enabled": false,
                  "first": 12,
                  "threshold-ms": 345
                }
                """;

            EntityHealthCheckConfig? result = JsonSerializer.Deserialize<EntityHealthCheckConfig>(json, Options);

            Assert.IsNotNull(result);
            Assert.IsFalse(result.Enabled);
            Assert.AreEqual(12, result.First);
            Assert.AreEqual(345, result.ThresholdMs);
            Assert.IsTrue(result.UserProvidedEnabled);
            Assert.IsTrue(result.UserProvidedFirst);
            Assert.IsTrue(result.UserProvidedThresholdMs);
        }

        [TestMethod]
        public void Deserialize_NullProperties_UsesDefaultsWithoutPresenceFlags()
        {
            const string json = """
                {
                  "enabled": null,
                  "first": null,
                  "threshold-ms": null
                }
                """;

            EntityHealthCheckConfig? result = JsonSerializer.Deserialize<EntityHealthCheckConfig>(json, Options);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Enabled);
            Assert.AreEqual(HealthCheckConstants.DEFAULT_FIRST_VALUE, result.First);
            Assert.AreEqual(HealthCheckConstants.DEFAULT_THRESHOLD_RESPONSE_TIME_MS, result.ThresholdMs);
            Assert.IsFalse(result.UserProvidedEnabled);
            Assert.IsFalse(result.UserProvidedFirst);
            Assert.IsFalse(result.UserProvidedThresholdMs);
        }

        [DataTestMethod]
        [DataRow("{\"first\":0}", "first")]
        [DataRow("{\"first\":-1}", "first")]
        [DataRow("{\"threshold-ms\":0}", "ttl-seconds")]
        [DataRow("{\"threshold-ms\":-1}", "ttl-seconds")]
        [DataRow("{\"unexpected\":1}", "Unexpected property")]
        public void Deserialize_InvalidValue_ThrowsJsonException(string json, string expectedMessage)
        {
            JsonException exception = Assert.ThrowsException<JsonException>(
                () => JsonSerializer.Deserialize<EntityHealthCheckConfig>(json, Options));

            StringAssert.Contains(exception.Message, expectedMessage);
        }

        [DataTestMethod]
        [DataRow("\"health\"")]
        [DataRow("42")]
        [DataRow("true")]
        [DataRow("[]")]
        [DataRow("{")]
        public void Deserialize_InvalidShape_ThrowsJsonException(string json)
        {
            Assert.ThrowsException<JsonException>(
                () => JsonSerializer.Deserialize<EntityHealthCheckConfig>(json, Options));
        }

        [TestMethod]
        public void Serialize_UserProvidedValues_WritesAllProperties()
        {
            EntityHealthCheckConfig value = new(enabled: false, first: 7, thresholdMs: 250);

            string json = JsonSerializer.Serialize(value, Options);
            using JsonDocument document = JsonDocument.Parse(json);

            Assert.IsFalse(document.RootElement.GetProperty("enabled").GetBoolean());
            Assert.AreEqual(7, document.RootElement.GetProperty("first").GetInt32());
            Assert.AreEqual(250, document.RootElement.GetProperty("threshold-ms").GetInt32());
        }

        [TestMethod]
        public void Serialize_OnlyEnabledProvided_OmitsDefaultOptionalValues()
        {
            EntityHealthCheckConfig value = new(enabled: true);

            string json = JsonSerializer.Serialize(value, Options);
            using JsonDocument document = JsonDocument.Parse(json);

            Assert.IsTrue(document.RootElement.GetProperty("enabled").GetBoolean());
            Assert.IsFalse(document.RootElement.TryGetProperty("first", out _));
            Assert.IsFalse(document.RootElement.TryGetProperty("threshold-ms", out _));
        }
    }
}
