// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class RuntimeOptionsConverterCoverageTests
    {
        private static JsonSerializerOptions Options => RuntimeConfigLoader.GetSerializationOptions();

        [TestMethod]
        public void GraphQLRuntimeOptions_FullObjectRoundTrips()
        {
            const string Json = """
                {
                  "enabled": true,
                  "allow-introspection": false,
                  "enable-aggregation": true,
                  "path": "/gql",
                  "depth-limit": 7,
                  "multiple-mutations": { "create": { "enabled": true } }
                }
                """;

            GraphQLRuntimeOptions? value = JsonSerializer.Deserialize<GraphQLRuntimeOptions>(Json, Options);
            string serialized = JsonSerializer.Serialize(value, Options);

            Assert.IsNotNull(value);
            Assert.IsTrue(value.Enabled);
            Assert.IsFalse(value.AllowIntrospection);
            Assert.IsTrue(value.EnableAggregation);
            Assert.AreEqual("/gql", value.Path);
            Assert.AreEqual(7, value.DepthLimit);
            Assert.IsTrue(value.MultipleMutationOptions?.MultipleCreateOptions?.Enabled);
            StringAssert.Contains(serialized, "\"multiple-mutations\"");
        }

        [DataTestMethod]
        [DataRow("true", true)]
        [DataRow("false", false)]
        public void GraphQLRuntimeOptions_BooleanShorthandDeserializes(string json, bool enabled)
        {
            GraphQLRuntimeOptions? value = JsonSerializer.Deserialize<GraphQLRuntimeOptions>(json, Options);

            Assert.IsNotNull(value);
            Assert.AreEqual(enabled, value.Enabled);
        }

        [TestMethod]
        public void GraphQLRuntimeOptions_NullDepthRoundTrips()
        {
            GraphQLRuntimeOptions? value = JsonSerializer.Deserialize<GraphQLRuntimeOptions>("{\"depth-limit\":null}", Options);
            string serialized = JsonSerializer.Serialize(value, Options);

            Assert.IsNotNull(value);
            Assert.IsTrue(value.UserProvidedDepthLimit);
            Assert.IsNull(value.DepthLimit);
            StringAssert.Contains(serialized, "\"depth-limit\": null");
        }

        [DataTestMethod]
        [DataRow("true", true)]
        [DataRow("false", false)]
        public void RestRuntimeOptions_BooleanShorthandDeserializes(string json, bool expectedEnabled)
        {
            RestRuntimeOptions? value = JsonSerializer.Deserialize<RestRuntimeOptions>(json, Options);

            Assert.IsNotNull(value);
            Assert.AreEqual(expectedEnabled, value.Enabled);
        }

        [DataTestMethod]
        [DataRow("{\"multiple-mutations\":{\"unknown\":true}}")]
        [DataRow("{\"multiple-mutations\":42}")]
        [DataRow("{\"multiple-mutations\":{\"create\":{\"unknown\":true}}}")]
        [DataRow("{\"multiple-mutations\":{\"create\":42}}")]
        public void GraphQLRuntimeOptions_InvalidMultipleMutationValuesThrow(string json)
        {
            Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<GraphQLRuntimeOptions>(json, Options));
        }

        [DataTestMethod]
        [DataRow("{\"enabled\":1}")]
        [DataRow("{\"allow-introspection\":1}")]
        [DataRow("{\"enable-aggregation\":1}")]
        [DataRow("{\"path\":false}")]
        [DataRow("{\"depth-limit\":0}")]
        [DataRow("{\"depth-limit\":-2}")]
        [DataRow("{\"depth-limit\":\"deep\"}")]
        [DataRow("{\"unknown\":true}")]
        [DataRow("42")]
        public void GraphQLRuntimeOptions_InvalidValuesThrow(string json)
        {
            Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<GraphQLRuntimeOptions>(json, Options));
        }

        [TestMethod]
        public void FileSinkOptions_FullObjectRoundTrips()
        {
            const string Json = """
                {
                  "enabled": true,
                  "path": "logs/dab.txt",
                  "rolling-interval": "day",
                  "retained-file-count-limit": 5,
                  "file-size-limit-bytes": 1024
                }
                """;

            FileSinkOptions? value = JsonSerializer.Deserialize<FileSinkOptions>(Json, Options);
            string serialized = JsonSerializer.Serialize(value, Options);

            Assert.IsNotNull(value);
            Assert.IsTrue(value.Enabled);
            Assert.AreEqual("logs/dab.txt", value.Path);
            Assert.AreEqual("Day", value.RollingInterval);
            Assert.AreEqual(5, value.RetainedFileCountLimit);
            Assert.AreEqual(1024L, value.FileSizeLimitBytes);
            StringAssert.Contains(serialized, "\"file-size-limit-bytes\"");
        }

        [DataTestMethod]
        [DataRow("{\"retained-file-count-limit\":0}")]
        [DataRow("{\"retained-file-count-limit\":-1}")]
        [DataRow("{\"unknown\":true}")]
        [DataRow("42")]
        public void FileSinkOptions_InvalidValuesThrow(string json)
        {
            Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<FileSinkOptions>(json, Options));
        }
    }
}
