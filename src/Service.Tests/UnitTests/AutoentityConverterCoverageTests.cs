// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class AutoentityConverterCoverageTests
    {
        private static JsonSerializerOptions Options => RuntimeConfigLoader.GetSerializationOptions();

        [TestMethod]
        public void Autoentity_WithPatterns_RoundTripsUserProvidedValues()
        {
            const string Json = """
                {
                  "patterns": {
                    "include": ["dbo.*", null],
                    "exclude": ["dbo.internal_*"],
                    "name": "generated_{object}"
                  },
                  "permissions": []
                }
                """;

            Autoentity? autoentity = JsonSerializer.Deserialize<Autoentity>(Json, Options);
            string serialized = JsonSerializer.Serialize(autoentity, Options);
            using JsonDocument document = JsonDocument.Parse(serialized);
            JsonElement patterns = document.RootElement.GetProperty("patterns");

            Assert.IsNotNull(autoentity);
            CollectionAssert.AreEqual(new[] { "dbo.*" }, autoentity.Patterns.Include);
            CollectionAssert.AreEqual(new[] { "dbo.internal_*" }, autoentity.Patterns.Exclude);
            Assert.AreEqual("generated_{object}", autoentity.Patterns.Name);
            Assert.AreEqual(1, patterns.GetProperty("include").GetArrayLength());
            Assert.AreEqual("generated_{object}", patterns.GetProperty("name").GetString());
        }

        [DataTestMethod]
        [DataRow("42", typeof(Autoentity))]
        [DataRow("42", typeof(AutoentityPatterns))]
        [DataRow("42", typeof(AutoentityTemplate))]
        [DataRow("{\"unexpected\":true}", typeof(Autoentity))]
        [DataRow("{\"unexpected\":true}", typeof(AutoentityPatterns))]
        [DataRow("{\"unexpected\":true}", typeof(AutoentityTemplate))]
        public void AutoentityConverters_InvalidInputThrows(string json, System.Type targetType)
        {
            Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize(json, targetType, Options));
        }

        [DataTestMethod]
        [DataRow("{\"include\":true}")]
        [DataRow("{\"exclude\":42}")]
        public void AutoentityPatterns_NonArrayPatternThrows(string json)
        {
            Assert.ThrowsException<JsonException>(() =>
                JsonSerializer.Deserialize<AutoentityPatterns>(json, Options));
        }

        [TestMethod]
        public void Autoentity_Write_EvaluatesEveryUserProvidedPatternFlag()
        {
            AutoentityPatterns[] patterns =
            {
                new(Include: new[] { "dbo.*" }),
                new(Exclude: new[] { "dbo.internal_*" }),
                new(Name: "generated_{object}")
            };

            foreach (AutoentityPatterns pattern in patterns)
            {
                string json = JsonSerializer.Serialize(new Autoentity(pattern, Template: null, Permissions: null), Options);
                StringAssert.Contains(json, "\"patterns\"");
            }
        }

        [TestMethod]
        public void Autoentity_Write_EvaluatesEveryUserProvidedTemplateFlag()
        {
            AutoentityTemplate[] templates =
            {
                new(Rest: new EntityRestOptions()),
                new(GraphQL: new EntityGraphQLOptions("Book", "Books")),
                new(Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: null)),
                new(Health: new EntityHealthCheckConfig(enabled: true)),
                new(Cache: new EntityCacheOptions(Enabled: true))
            };

            foreach (AutoentityTemplate template in templates)
            {
                string json = JsonSerializer.Serialize(new Autoentity(Patterns: null, template, Permissions: null), Options);
                StringAssert.Contains(json, "\"template\"");
            }
        }
    }
}
