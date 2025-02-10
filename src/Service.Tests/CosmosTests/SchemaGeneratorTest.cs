// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Generator;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    /// <summary>
    /// Contains unit tests for the <see cref="SchemaGenerator"/> class to verify its schema generation capabilities 
    /// based on various JSON data inputs. This class ensures that the schema generation logic is accurate and 
    /// robust for different data structures and configurations.
    /// </summary>
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class SchemaGeneratorTest
    {
        /// <summary>
        /// Tests the <see cref="SchemaGenerator.Generate"/> method using a single JSON file to generate a GraphQL schema.
        /// Verifies that the generated schema matches the expected schema output.
        /// </summary>
        /// <param name="jsonFilePath">Path to the directory containing the input JSON file.</param>
        /// <param name="gqlFilePath">Path to the directory containing the expected GraphQL schema file.</param>
        [TestMethod]
        [DataRow("CosmosTests/TestData/CosmosData", "CosmosTests/TestData/GeneratedGqlSchema")]
        public void TestSchemaGenerator(string jsonFilePath, string gqlFilePath)
        {
            string json = Regex.Replace(File.ReadAllText($"{jsonFilePath}/EmulatorData.json", Encoding.UTF8), @"\s+", string.Empty);
            List<JsonDocument> jsonArray = new() { JsonSerializer.Deserialize<JsonDocument>(json) };

            string actualSchema = SchemaGenerator.Generate(jsonArray, "containerName");
            string expectedSchema = File.ReadAllText($"{gqlFilePath}/EmulatorData.gql");

            Assert.AreEqual(expectedSchema, actualSchema);
        }

        /// <summary>
        /// Tests the <see cref="SchemaGenerator.Generate"/> method using multiple JSON files to generate a GraphQL schema.
        /// The test can optionally use a runtime configuration file for schema generation.
        /// </summary>
        /// <param name="jsonFilePath">Path to the directory containing multiple JSON files for input.</param>
        /// <param name="gqlFilePath">Path to the directory containing the expected GraphQL schema file.</param>
        /// <param name="useConfigFilePath">Boolean flag indicating whether to use a runtime configuration file.</param>
        [TestMethod]
        [DataRow("CosmosTests/TestData/CosmosData/MultiItems", "CosmosTests/TestData/GeneratedGqlSchema", false)]
        [DataRow("CosmosTests/TestData/CosmosData/MultiItems", "CosmosTests/TestData/GeneratedGqlSchema", true)]
        public void TestSchemaGeneratorUsingMultipleJson(string jsonFilePath, string gqlFilePath, bool useConfigFilePath)
        {
            RuntimeConfig baseConfig = null;
            string gqlFileName = "MultiItems.gql";
            if (useConfigFilePath)
            {
                TestHelper.SetupDatabaseEnvironment(TestCategory.COSMOSDBNOSQL);
                FileSystemRuntimeConfigLoader baseLoader = TestHelper.GetRuntimeConfigLoader();
                if (!baseLoader.TryLoadKnownConfig(out baseConfig))
                {
                    throw new ApplicationException("Failed to load the default CosmosDB_NoSQL config and cannot continue with tests.");
                }

                gqlFileName = "MultiItemsWithConfig.gql";
            }

            List<JsonDocument> jArray = new();

            string[] successPayloadFiles = Directory.GetFiles(jsonFilePath, "*.json");
            foreach (string payloadFile in successPayloadFiles)
            {
                string json = Regex.Replace(File.ReadAllText(payloadFile, Encoding.UTF8), @"\s+", string.Empty);
                jArray.Add(JsonSerializer.Deserialize<JsonDocument>(json));
            }

            string actualSchema = SchemaGenerator.Generate(jArray, "planet", baseConfig);
            string expectedSchema = File.ReadAllText($"{gqlFilePath}/{gqlFileName}");

            Assert.AreEqual(expectedSchema, actualSchema);
        }

        /// <summary>
        /// Tests the <see cref="SchemaGenerator.Generate"/> method with a JSON object containing mixed data types.
        /// Verifies that the generated GraphQL schema correctly represents the structure and types of the input data.
        /// </summary>
        [TestMethod]
        public void TestMixDataJsonObject()
        {
            List<JsonDocument> jsonArray = new() {
                JsonDocument.Parse(@"{
                  ""id"": 12345,
                  ""name"": ""Widget"",
                  ""price"": 19.99,
                  ""inStock"": true,
                  ""tags"": [ ""gadget"", ""tool"", ""home"" ],
                  ""dimensions"": {
                    ""length"": 10.5,
                    ""width"": 7.25,
                    ""height"": 3.0
                  },
                  ""manufacturedDate"": ""2021-08-15T08:00:00Z"",
                  ""relatedProducts"": [
                    23456,
                    34567,
                    45678
                  ]
                }
                ")};

            string actualSchema = SchemaGenerator.Generate(jsonArray, "containerName");

            string expectedSchema = @"type ContainerName @model(name: ""ContainerName"") {
  id: ID!,
  name: String!,
  price: Float!,
  inStock: Boolean!,
  tags: [String]!,
  dimensions: Dimensions!,
  manufacturedDate: Date!,
  relatedProducts: [Int]!
}
type Dimensions {
  length: Float!,
  width: Float!,
  height: Float!
}
";

            Assert.AreEqual(expectedSchema, actualSchema);
        }

        /// <summary>
        /// Tests the <see cref="SchemaGenerator.Generate"/> method with a complex JSON object containing nested objects and arrays.
        /// Ensures that the generated GraphQL schema accurately represents the nested structure and data types.
        /// </summary>
        [TestMethod]
        public void TestComplexJsonObject()
        {
            List<JsonDocument> jsonArray = new() {
                JsonDocument.Parse(@"{
                  ""name"": ""John Doe"",
                  ""age"": 30,
                  ""address"": {
                    ""street"": ""123 Main St"",
                    ""city"": ""Anytown"",
                    ""state"": ""CA"",
                    ""zip"": ""12345"",
                    ""coordinates"": {
                      ""latitude"": 34.0522,
                      ""longitude"": -118.2437
                    }
                  },
                  ""emails"": [
                    ""john.doe@example.com"",
                    ""john.doe@work.com""
                  ],
                  ""phoneNumbers"": [
                    {
                      ""type"": ""home"",
                      ""number"": ""555-555-5555""
                    },
                    {
                      ""type"": ""work"",
                      ""number"": ""555-555-5556""
                    }
                  ]
                }")};

            string actualSchema = SchemaGenerator.Generate(jsonArray, "containerName");

            string expectedSchema = @"type ContainerName @model(name: ""ContainerName"") {
  name: String!,
  age: Int!,
  address: Address!,
  emails: [String]!,
  phoneNumbers: [PhoneNumber]!
}
type Address {
  street: String!,
  city: String!,
  state: String!,
  zip: String!,
  coordinates: Coordinates!
}
type Coordinates {
  latitude: Float!,
  longitude: Float!
}
type PhoneNumber {
  type: String!,
  number: String!
}
";

            Assert.AreEqual(expectedSchema, actualSchema);
        }

        /// <summary>
        /// Tests the <see cref="SchemaGenerator.Generate"/> method with a JSON array containing mixed data types.
        /// Verifies that the generated GraphQL schema includes all possible fields and types present in the input data.
        /// </summary>
        [TestMethod]
        public void TestMixedJsonArray()
        {
            List<JsonDocument> jsonArray = new() {
                JsonDocument.Parse(@"{ ""name"": ""John"", ""age"": 30, ""isStudent"": false, ""birthDate"": ""1980-01-01T00:00:00Z"" }"),
                JsonDocument.Parse(@"{ ""email"": ""john@example.com"", ""phone"": ""123-456-7890"" }")};

            string actualSchema = SchemaGenerator.Generate(jsonArray, "containerName");

            string expectedSchema = @"type ContainerName @model(name: ""ContainerName"") {
  name: String,
  age: Int,
  isStudent: Boolean,
  birthDate: Date,
  email: String,
  phone: String
}
";

            Assert.AreEqual(expectedSchema, actualSchema);
        }

        /// <summary>
        /// Tests the <see cref="SchemaGenerator.Generate"/> method with an empty JSON array.
        /// </summary>
        [TestMethod]
        public void TestEmptyJsonArray()
        {
            List<JsonDocument> jsonArray = new();
            Assert.AreEqual(string.Empty, SchemaGenerator.Generate(jsonArray, "containerName"));
        }

        /// <summary>
        /// Tests the <see cref="SchemaGenerator.Generate"/> method with an empty JSON array.
        /// Ensures that the method correctly handles an empty input.
        /// </summary>
        [TestMethod]
        public void TestEmptyJsonArrayInPayload()
        {
            List<JsonDocument> jsonArray = new() {
                JsonDocument.Parse(@"{ ""name"": ""John"", ""product"": [], ""isStudent"": false, ""birthDate"": ""1980-01-01T00:00:00Z"" }")};
            string actualSchema = SchemaGenerator.Generate(jsonArray, "containerName");

            string expectedSchema = @"type ContainerName @model(name: ""ContainerName"") {
  name: String!,
  isStudent: Boolean!,
  birthDate: Date!
}
";
            Assert.AreEqual(expectedSchema, actualSchema);
        }

        /// <summary>
        /// Tests the <see cref="SchemaGenerator.Generate"/> method with a JSON array containing a null object.
        /// Ensures that the method handles null objects gracefully and throws an appropriate exception.
        /// </summary>
        [TestMethod]
        public void TestArrayContainingNullObject()
        {
            List<JsonDocument> jsonArray = new();
            jsonArray.Add(null);

            Assert.ThrowsException<InvalidOperationException>(() => SchemaGenerator.Generate(jsonArray, "containerName"));
        }

        /// <summary>
        /// Tests the <see cref="SchemaGenerator.Generate"/> method with a JSON array where some elements are null.
        /// Verifies that the generated GraphQL schema represents fields with null values as nullable types.
        /// </summary>
        [TestMethod]
        public void TestJsonArrayWithNullElement()
        {
            JsonDocument jsonArray = JsonDocument.Parse(@"[{ ""name"": ""John"", ""age"": null }]");

            string actualSchema = SchemaGenerator.Generate(jsonArray.RootElement.EnumerateArray().Select(item => item.Deserialize<JsonDocument>()).ToList(), "containerName");

            string expectedSchema = @"type ContainerName @model(name: ""ContainerName"") {
  name: String!,
  age: String!
}
";

            Assert.AreEqual(expectedSchema, actualSchema);
        }
    }
}
