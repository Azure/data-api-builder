// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable disable

using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <c>EmbeddingsOptionsConverterFactory</c> covering JSON read/write
    /// of embedding service options, including nested endpoint, health, chunking and cache.
    /// </summary>
    [TestClass]
    public class EmbeddingsOptionsConverterTests
    {
        private static JsonSerializerOptions GetOptions()
        {
            return RuntimeConfigLoader.GetSerializationOptions();
        }

        [TestMethod]
        public void Deserialize_AllProperties_AreReadCorrectly()
        {
            string json = @"{
                ""enabled"": true,
                ""provider"": ""azure-openai"",
                ""base-url"": ""https://contoso.openai.azure.com"",
                ""api-key"": ""secret-key"",
                ""model"": ""text-embedding-3-small"",
                ""api-version"": ""2023-05-15"",
                ""dimensions"": 1536,
                ""timeout-ms"": 5000,
                ""endpoint"": {
                    ""enabled"": true,
                    ""roles"": [ ""authenticated"" ]
                },
                ""health"": {
                    ""enabled"": true,
                    ""threshold-ms"": 1000,
                    ""test-text"": ""hello"",
                    ""expected-dimensions"": 1536
                },
                ""chunking"": {
                    ""enabled"": true,
                    ""size-chars"": 500,
                    ""overlap-chars"": 50
                },
                ""cache"": {
                    ""enabled"": true,
                    ""ttl-hours"": 24
                }
            }";

            EmbeddingsOptions options = JsonSerializer.Deserialize<EmbeddingsOptions>(json, GetOptions());

            Assert.IsNotNull(options);
            Assert.IsTrue(options.Enabled);
            Assert.AreEqual(EmbeddingProviderType.AzureOpenAI, options.Provider);
            Assert.AreEqual("https://contoso.openai.azure.com", options.BaseUrl);
            Assert.AreEqual("secret-key", options.ApiKey);
            Assert.AreEqual("text-embedding-3-small", options.Model);
            Assert.AreEqual("2023-05-15", options.ApiVersion);
            Assert.AreEqual(1536, options.Dimensions);
            Assert.AreEqual(5000, options.TimeoutMs);
            Assert.IsNotNull(options.Endpoint);
            Assert.IsTrue(options.Endpoint.Enabled);
            CollectionAssert.AreEqual(new[] { "authenticated" }, options.Endpoint.Roles);
            Assert.IsNotNull(options.Health);
            Assert.IsTrue(options.Health.Enabled);
            Assert.IsNotNull(options.Chunking);
            Assert.IsTrue(options.Chunking.Enabled);
            Assert.IsNotNull(options.Cache);
            Assert.IsTrue(options.Cache.Enabled ?? false);
        }

        [TestMethod]
        public void Deserialize_MinimalRequiredProperties_UsesDefaults()
        {
            string json = @"{
                ""provider"": ""openai"",
                ""base-url"": ""https://api.openai.com"",
                ""api-key"": ""secret-key""
            }";

            EmbeddingsOptions options = JsonSerializer.Deserialize<EmbeddingsOptions>(json, GetOptions());

            Assert.IsNotNull(options);
            Assert.AreEqual(EmbeddingProviderType.OpenAI, options.Provider);
            Assert.IsTrue(options.Enabled, "Enabled defaults to true when not provided.");
            Assert.IsNull(options.Model);
            Assert.IsNull(options.ApiVersion);
            Assert.AreEqual(EmbeddingsOptions.DEFAULT_DIMENSIONS, options.Dimensions, "Dimensions defaults to DEFAULT_DIMENSIONS when not provided.");
            Assert.IsNull(options.TimeoutMs);
            Assert.IsNull(options.Endpoint);
            Assert.IsNull(options.Health);
            Assert.IsNull(options.Chunking);
            Assert.IsNull(options.Cache);
        }

        [TestMethod]
        public void Deserialize_UnknownProperty_IsSkipped()
        {
            string json = @"{
                ""provider"": ""openai"",
                ""base-url"": ""https://api.openai.com"",
                ""api-key"": ""secret-key"",
                ""unknown-property"": { ""nested"": [ 1, 2, 3 ] }
            }";

            EmbeddingsOptions options = JsonSerializer.Deserialize<EmbeddingsOptions>(json, GetOptions());

            Assert.IsNotNull(options);
            Assert.AreEqual(EmbeddingProviderType.OpenAI, options.Provider);
        }

        [TestMethod]
        public void Deserialize_UnknownProvider_ThrowsJsonException()
        {
            string json = @"{
                ""provider"": ""unsupported-provider"",
                ""base-url"": ""https://api.openai.com"",
                ""api-key"": ""secret-key""
            }";

            Assert.ThrowsException<JsonException>(
                () => JsonSerializer.Deserialize<EmbeddingsOptions>(json, GetOptions()));
        }

        [DataTestMethod]
        [DataRow(@"{ ""base-url"": ""https://api.openai.com"", ""api-key"": ""k"" }", DisplayName = "Missing provider")]
        [DataRow(@"{ ""provider"": ""openai"", ""api-key"": ""k"" }", DisplayName = "Missing base-url")]
        [DataRow(@"{ ""provider"": ""openai"", ""base-url"": ""https://api.openai.com"" }", DisplayName = "Missing api-key")]
        public void Deserialize_MissingRequiredProperty_ThrowsJsonException(string json)
        {
            Assert.ThrowsException<JsonException>(
                () => JsonSerializer.Deserialize<EmbeddingsOptions>(json, GetOptions()));
        }

        [TestMethod]
        public void Deserialize_NonObjectToken_ThrowsJsonException()
        {
            Assert.ThrowsException<JsonException>(
                () => JsonSerializer.Deserialize<EmbeddingsOptions>("\"not-an-object\"", GetOptions()));
        }

        [TestMethod]
        public void Serialize_MinimalOptions_OmitsUnsetOptionalProperties()
        {
            string json = @"{
                ""provider"": ""openai"",
                ""base-url"": ""https://api.openai.com"",
                ""api-key"": ""secret-key""
            }";

            EmbeddingsOptions options = JsonSerializer.Deserialize<EmbeddingsOptions>(json, GetOptions());
            string serialized = JsonSerializer.Serialize(options, GetOptions());
            JObject jObject = JObject.Parse(serialized);

            Assert.AreEqual("openai", jObject["provider"].Value<string>());
            Assert.AreEqual("https://api.openai.com", jObject["base-url"].Value<string>());
            Assert.AreEqual("secret-key", jObject["api-key"].Value<string>());
            Assert.IsFalse(jObject.ContainsKey("model"));
            Assert.IsFalse(jObject.ContainsKey("endpoint"));
            Assert.IsFalse(jObject.ContainsKey("health"));
            Assert.IsFalse(jObject.ContainsKey("chunking"));
            Assert.IsFalse(jObject.ContainsKey("cache"));
        }

        [TestMethod]
        public void RoundTrip_PreservesScalarAndNestedValues()
        {
            string json = @"{
                ""enabled"": false,
                ""provider"": ""azure-openai"",
                ""base-url"": ""https://contoso.openai.azure.com"",
                ""api-key"": ""secret-key"",
                ""model"": ""my-deployment"",
                ""api-version"": ""2023-05-15"",
                ""dimensions"": 768,
                ""timeout-ms"": 12000,
                ""endpoint"": {
                    ""enabled"": true,
                    ""roles"": [ ""authenticated"", ""reader"" ]
                }
            }";

            EmbeddingsOptions original = JsonSerializer.Deserialize<EmbeddingsOptions>(json, GetOptions());
            string serialized = JsonSerializer.Serialize(original, GetOptions());
            EmbeddingsOptions result = JsonSerializer.Deserialize<EmbeddingsOptions>(serialized, GetOptions());

            Assert.AreEqual(original.Enabled, result.Enabled);
            Assert.AreEqual(original.Provider, result.Provider);
            Assert.AreEqual(original.BaseUrl, result.BaseUrl);
            Assert.AreEqual(original.ApiKey, result.ApiKey);
            Assert.AreEqual(original.Model, result.Model);
            Assert.AreEqual(original.ApiVersion, result.ApiVersion);
            Assert.AreEqual(original.Dimensions, result.Dimensions);
            Assert.AreEqual(original.TimeoutMs, result.TimeoutMs);
            Assert.IsNotNull(result.Endpoint);
            Assert.AreEqual(original.Endpoint.Enabled, result.Endpoint.Enabled);
            CollectionAssert.AreEqual(original.Endpoint.Roles, result.Endpoint.Roles);
        }
    }
}
