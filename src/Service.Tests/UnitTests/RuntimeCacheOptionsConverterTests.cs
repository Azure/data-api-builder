// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable disable

using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <c>RuntimeCacheOptionsConverterFactory</c> and
    /// <c>RuntimeCacheLevel2OptionsConverterFactory</c> covering JSON read/write of the
    /// runtime (L1) and L2 cache options.
    /// </summary>
    [TestClass]
    public class RuntimeCacheOptionsConverterTests
    {
        private static JsonSerializerOptions GetOptions()
        {
            return RuntimeConfigLoader.GetSerializationOptions();
        }

        [TestMethod]
        public void Deserialize_RuntimeCache_AllProperties()
        {
            string json = @"{
                ""enabled"": true,
                ""ttl-seconds"": 30,
                ""level-2"": {
                    ""enabled"": true,
                    ""provider"": ""redis"",
                    ""connection-string"": ""localhost:6379"",
                    ""partition"": ""dab""
                }
            }";

            RuntimeCacheOptions options = JsonSerializer.Deserialize<RuntimeCacheOptions>(json, GetOptions());

            Assert.IsNotNull(options);
            Assert.IsTrue(options.Enabled ?? false);
            Assert.AreEqual(30, options.TtlSeconds);
            Assert.IsTrue(options.UserProvidedTtlOptions);
            Assert.IsNotNull(options.Level2);
            Assert.IsTrue(options.Level2.Enabled ?? false);
            Assert.AreEqual("redis", options.Level2.Provider);
            Assert.AreEqual("localhost:6379", options.Level2.ConnectionString);
            Assert.AreEqual("dab", options.Level2.Partition);
        }

        [TestMethod]
        public void Deserialize_RuntimeCache_NoTtl_UsesDefault()
        {
            RuntimeCacheOptions options = JsonSerializer.Deserialize<RuntimeCacheOptions>(@"{ ""enabled"": true }", GetOptions());

            Assert.IsNotNull(options);
            Assert.AreEqual(RuntimeCacheOptions.DEFAULT_TTL_SECONDS, options.TtlSeconds);
            Assert.IsFalse(options.UserProvidedTtlOptions);
        }

        [DataTestMethod]
        [DataRow(@"{ ""ttl-seconds"": 0 }", DisplayName = "Zero ttl-seconds")]
        [DataRow(@"{ ""ttl-seconds"": -10 }", DisplayName = "Negative ttl-seconds")]
        public void Deserialize_RuntimeCache_InvalidTtl_ThrowsJsonException(string json)
        {
            Assert.ThrowsException<JsonException>(
                () => JsonSerializer.Deserialize<RuntimeCacheOptions>(json, GetOptions()));
        }

        [TestMethod]
        public void Serialize_RuntimeCache_UserProvidedTtl_IsWritten()
        {
            RuntimeCacheOptions options = new(Enabled: true, TtlSeconds: 45);

            string json = JsonSerializer.Serialize(options, GetOptions());
            JObject jObject = JObject.Parse(json);

            Assert.IsTrue(jObject["enabled"].Value<bool>());
            Assert.AreEqual(45, jObject["ttl-seconds"].Value<int>());
        }

        [TestMethod]
        public void Serialize_RuntimeCache_NoUserTtl_OmitsTtl()
        {
            RuntimeCacheOptions options = new(Enabled: false);

            string json = JsonSerializer.Serialize(options, GetOptions());
            JObject jObject = JObject.Parse(json);

            Assert.IsFalse(jObject["enabled"].Value<bool>());
            Assert.IsFalse(jObject.ContainsKey("ttl-seconds"));
        }

        [TestMethod]
        public void Serialize_RuntimeCache_WithLevel2_WritesLevel2()
        {
            RuntimeCacheOptions options = new(Enabled: true, TtlSeconds: 10)
            {
                Level2 = new RuntimeCacheLevel2Options(Enabled: true, Provider: "redis")
            };

            string json = JsonSerializer.Serialize(options, GetOptions());
            JObject jObject = JObject.Parse(json);

            Assert.IsTrue(jObject.ContainsKey("level-2"));
            Assert.AreEqual("redis", jObject["level-2"]["provider"].Value<string>());
        }

        [TestMethod]
        public void Deserialize_Level2_AllProperties()
        {
            string json = @"{
                ""enabled"": true,
                ""provider"": ""redis"",
                ""connection-string"": ""localhost:6379"",
                ""partition"": ""p1""
            }";

            RuntimeCacheLevel2Options level2 = JsonSerializer.Deserialize<RuntimeCacheLevel2Options>(json, GetOptions());

            Assert.IsNotNull(level2);
            Assert.IsTrue(level2.Enabled ?? false);
            Assert.AreEqual("redis", level2.Provider);
            Assert.AreEqual("localhost:6379", level2.ConnectionString);
            Assert.AreEqual("p1", level2.Partition);
        }

        [TestMethod]
        public void Serialize_Level2_OnlyWritesProvidedProperties()
        {
            RuntimeCacheLevel2Options level2 = new(Enabled: false);

            string json = JsonSerializer.Serialize(level2, GetOptions());
            JObject jObject = JObject.Parse(json);

            Assert.IsFalse(jObject["enabled"].Value<bool>());
            Assert.IsFalse(jObject.ContainsKey("provider"));
            Assert.IsFalse(jObject.ContainsKey("connection-string"));
            Assert.IsFalse(jObject.ContainsKey("partition"));
        }

        [TestMethod]
        public void RoundTrip_RuntimeCache_PreservesValues()
        {
            RuntimeCacheOptions original = new(Enabled: true, TtlSeconds: 25)
            {
                Level2 = new RuntimeCacheLevel2Options(Enabled: true, Provider: "redis", ConnectionString: "localhost:6379", Partition: "p")
            };

            string json = JsonSerializer.Serialize(original, GetOptions());
            RuntimeCacheOptions result = JsonSerializer.Deserialize<RuntimeCacheOptions>(json, GetOptions());

            Assert.AreEqual(original.Enabled, result.Enabled);
            Assert.AreEqual(original.TtlSeconds, result.TtlSeconds);
            Assert.IsNotNull(result.Level2);
            Assert.AreEqual(original.Level2.Provider, result.Level2.Provider);
            Assert.AreEqual(original.Level2.ConnectionString, result.Level2.ConnectionString);
            Assert.AreEqual(original.Level2.Partition, result.Level2.Partition);
        }
    }
}
