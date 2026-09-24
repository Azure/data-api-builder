// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
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
        [DataRow(null, true, DisplayName = "Missing sections default to enabled")]
        [DataRow(true, true, DisplayName = "Explicitly enabled sections are enabled")]
        [DataRow(false, false, DisplayName = "Explicitly disabled sections are disabled")]
        public void RuntimeOptions_EnablementProperties_DefaultUnlessExplicitlyDisabled(bool? enabled, bool expected)
        {
            RuntimeOptions options = new(
                Rest: enabled.HasValue ? new RestRuntimeOptions(Enabled: enabled.Value) : null,
                GraphQL: enabled.HasValue ? new GraphQLRuntimeOptions(Enabled: enabled.Value) : null,
                Mcp: enabled.HasValue ? new McpRuntimeOptions(Enabled: enabled.Value) : null,
                Host: null,
                Health: enabled.HasValue ? new RuntimeHealthCheckConfig(enabled.Value) : null);

            Assert.AreEqual(expected, options.IsRestEnabled);
            Assert.AreEqual(expected, options.IsGraphQLEnabled);
            Assert.AreEqual(expected, options.IsMcpEnabled);
            Assert.AreEqual(expected, options.IsHealthCheckEnabled);
        }

        [DataTestMethod]
        [DataRow("rest")]
        [DataRow("graphql")]
        [DataRow("mcp")]
        [DataRow("health")]
        public void RuntimeOptions_EnablementProperties_EvaluateEachSectionIndependently(string disabledSection)
        {
            RuntimeOptions options = new(
                Rest: new RestRuntimeOptions(Enabled: disabledSection != "rest"),
                GraphQL: new GraphQLRuntimeOptions(Enabled: disabledSection != "graphql"),
                Mcp: new McpRuntimeOptions(Enabled: disabledSection != "mcp"),
                Host: null,
                Health: new RuntimeHealthCheckConfig(enabled: disabledSection != "health"));

            Assert.AreEqual(disabledSection != "rest", options.IsRestEnabled);
            Assert.AreEqual(disabledSection != "graphql", options.IsGraphQLEnabled);
            Assert.AreEqual(disabledSection != "mcp", options.IsMcpEnabled);
            Assert.AreEqual(disabledSection != "health", options.IsHealthCheckEnabled);
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

        /// <summary>
        /// Verifies explicitly configured health options serialize as an object while an untouched default configuration serializes as null.
        /// </summary>
        [TestMethod]
        public void RuntimeHealthOptions_WriteConfiguredAndDefaultForms()
        {
            RuntimeHealthCheckConfig configured = new(
                enabled: true,
                roles: new HashSet<string> { "reader" },
                cacheTtlSeconds: 12,
                maxQueryParallelism: 3);

            string configuredJson = JsonSerializer.Serialize(configured, Options);
            string defaultJson = JsonSerializer.Serialize(new RuntimeHealthCheckConfig(), Options);

            StringAssert.Contains(configuredJson, "\"enabled\": true");
            StringAssert.Contains(configuredJson, "\"cache-ttl-seconds\": 12");
            StringAssert.Contains(configuredJson, "\"roles\"");
            StringAssert.Contains(configuredJson, "\"max-query-parallelism\": 3");
            Assert.AreEqual("null", defaultJson);
        }

        /// <summary>
        /// Verifies any independently supplied data-source health property causes that property to be serialized.
        /// </summary>
        [TestMethod]
        public void DatasourceHealthOptions_WriteEachUserProvidedTrigger()
        {
            string enabled = JsonSerializer.Serialize(new DatasourceHealthCheckConfig(enabled: true), Options);
            string named = JsonSerializer.Serialize(new DatasourceHealthCheckConfig(enabled: null, name: "primary"), Options);
            string threshold = JsonSerializer.Serialize(new DatasourceHealthCheckConfig(enabled: null, thresholdMs: 42), Options);

            StringAssert.Contains(enabled, "\"enabled\": true");
            StringAssert.Contains(named, "\"name\": \"primary\"");
            StringAssert.Contains(threshold, "\"threshold-ms\": 42");
        }
    }
}
