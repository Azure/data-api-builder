// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class EntityApiOptionsConverterCoverageTests
    {
        private static JsonSerializerOptions Options => RuntimeConfigLoader.GetSerializationOptions();

        [TestMethod]
        public void RestOptions_ObjectReadsAllPropertiesAndWritesThem()
        {
            const string Json = """
                { "path": "/books", "methods": ["get", "post"], "enabled": false }
                """;

            EntityRestOptions? options = JsonSerializer.Deserialize<EntityRestOptions>(Json, Options);
            string serialized = JsonSerializer.Serialize(options, Options);

            Assert.IsNotNull(options);
            Assert.AreEqual("/books", options.Path);
            CollectionAssert.AreEqual(new[] { SupportedHttpVerb.Get, SupportedHttpVerb.Post }, options.Methods);
            Assert.IsFalse(options.Enabled);
            StringAssert.Contains(serialized, "\"methods\"");
        }

        [DataTestMethod]
        [DataRow("\"/books\"", "/books", true)]
        [DataRow("true", null, true)]
        [DataRow("false", null, false)]
        public void RestOptions_ShorthandFormsDeserialize(string json, string? expectedPath, bool expectedEnabled)
        {
            EntityRestOptions? options = JsonSerializer.Deserialize<EntityRestOptions>(json, Options);

            Assert.IsNotNull(options);
            Assert.AreEqual(expectedPath, options.Path);
            Assert.AreEqual(expectedEnabled, options.Enabled);
        }

        [DataTestMethod]
        [DataRow("{\"path\":42}")]
        [DataRow("{\"unexpected\":true}")]
        [DataRow("42")]
        public void RestOptions_InvalidFormsThrow(string json)
        {
            Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<EntityRestOptions>(json, Options));
        }

        [TestMethod]
        public void RestOptions_WriteNullPath_WhenNullsAreNotIgnored()
        {
            JsonSerializerOptions options = new(Options) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

            string json = JsonSerializer.Serialize(new EntityRestOptions(Array.Empty<SupportedHttpVerb>(), null, true), options);
            using JsonDocument document = JsonDocument.Parse(json);

            Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("path").ValueKind);
        }

        [TestMethod]
        public void GraphQLOptions_ObjectReadsNestedTypeAndOperationAndWritesThem()
        {
            const string Json = """
                {
                  "enabled": true,
                  "type": { "singular": "book", "ignored": "value", "plural": "books" },
                  "operation": "mutation"
                }
                """;

            EntityGraphQLOptions? options = JsonSerializer.Deserialize<EntityGraphQLOptions>(Json, Options);
            string serialized = JsonSerializer.Serialize(options, Options);

            Assert.IsNotNull(options);
            Assert.AreEqual("book", options.Singular);
            Assert.AreEqual("books", options.Plural);
            Assert.AreEqual(GraphQLOperation.Mutation, options.Operation);
            StringAssert.Contains(serialized, "\"operation\"");
        }

        [DataTestMethod]
        [DataRow("true", "", true)]
        [DataRow("false", "", false)]
        [DataRow("\"book\"", "book", true)]
        public void GraphQLOptions_ShorthandFormsDeserialize(string json, string expectedSingular, bool expectedEnabled)
        {
            EntityGraphQLOptions? options = JsonSerializer.Deserialize<EntityGraphQLOptions>(json, Options);

            Assert.IsNotNull(options);
            Assert.AreEqual(expectedSingular, options.Singular);
            Assert.AreEqual(expectedEnabled, options.Enabled);
        }

        [DataTestMethod]
        [DataRow("{\"type\":[]}")]
        [DataRow("42")]
        public void GraphQLOptions_InvalidFormsThrow(string json)
        {
            Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<EntityGraphQLOptions>(json, Options));
        }

        [TestMethod]
        public void GraphQLOptions_WriteNullOperation_WhenNullsAreNotIgnored()
        {
            JsonSerializerOptions options = new(Options) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

            string json = JsonSerializer.Serialize(new EntityGraphQLOptions("book", "books", true), options);
            using JsonDocument document = JsonDocument.Parse(json);

            Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("operation").ValueKind);
        }

        [TestMethod]
        public void EntityAction_ObjectWithoutExcludeNormalizesToEmptyCollection()
        {
            EntityAction? action = JsonSerializer.Deserialize<EntityAction>(
                "{\"action\":\"read\",\"fields\":{\"include\":[\"id\"]}}", Options);

            Assert.IsNotNull(action?.Fields?.Exclude);
            Assert.AreEqual(0, action.Fields.Exclude.Count);
        }

        [TestMethod]
        public void EntityCacheOptions_NonObjectThrows()
        {
            Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<EntityCacheOptions>("true", Options));
        }
    }
}
