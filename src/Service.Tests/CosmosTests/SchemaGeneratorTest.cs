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
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class SchemaGeneratorTest
    {
        [TestMethod]
        [DataRow("CosmosTests/TestData/CosmosData", "CosmosTests/TestData/GeneratedGqlSchema")]
        public void TestSchemaGenerator(string jsonFilePath, string gqlFilePath)
        {
            string json = Regex.Replace(File.ReadAllText($"{jsonFilePath}/EmulatorData.json", Encoding.UTF8), @"\s+", string.Empty);
            List<JsonDocument> jsonArray = new() { JsonSerializer.Deserialize<JsonDocument>(json) };

            string actualSchema = SchemaGenerator.Generate(jsonArray, "containerName");
            string expectedSchema = File.ReadAllText($"{gqlFilePath}/EmulatorData.gql");

            AreEqualAfterCleanup(expectedSchema, actualSchema);
        }

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

            AreEqualAfterCleanup(expectedSchema, actualSchema);
        }

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

            string gqlSchema = SchemaGenerator.Generate(jsonArray, "containerName");

            string expectedSchema = @"type ContainerName @model {
  id : ID!,
  name : String!,
  price : Float!,
  inStock : Boolean!,
  tags : [String]!,
  dimensions : Dimensions!,
  manufacturedDate : Date!,
  relatedProducts : [Int]!
}
type Dimensions {
  length : Float!,
  width : Float!,
  height : Float!
}";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
        }

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

            string gqlSchema = SchemaGenerator.Generate(jsonArray, "containerName");

            string expectedSchema = @"type ContainerName @model {
  name : String!,
  age : Int!,
  address : Address!,
  emails : [String]!,
  phoneNumbers : [PhoneNumber]!
}
type Address {
  street : String!,
  city : String!,
  state : String!,
  zip : String!,
  coordinates : Coordinates!
}
type Coordinates {
  latitude : Float!,
  longitude : Float!
}
type PhoneNumber {
  type : String!,
  number : String!
}";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
        }

        [TestMethod]
        public void TestMixedJsonArray()
        {
            List<JsonDocument> jsonArray = new() {
                JsonDocument.Parse(@"{ ""name"": ""John"", ""age"": 30, ""isStudent"": false, ""birthDate"": ""1980-01-01T00:00:00Z"" }"),
                JsonDocument.Parse(@"{ ""email"": ""john@example.com"", ""phone"": ""123-456-7890"" }")};

            string gqlSchema = SchemaGenerator.Generate(jsonArray, "containerName");

            string expectedSchema = @"type ContainerName @model {
              name: String,
              age: Int,
              isStudent: Boolean,
              birthDate: Date,
              email: String,
              phone: String
            }";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
        }

        [TestMethod]
        public void TestEmptyJsonArray()
        {
            List<JsonDocument> jsonArray = new();
            Assert.ThrowsException<InvalidOperationException>(() => SchemaGenerator.Generate(jsonArray, "containerName"));
        }

        [TestMethod]
        public void TestArrayContainingNullObject()
        {
            List<JsonDocument> jsonArray = new();
            jsonArray.Add(null);

            Assert.ThrowsException<InvalidOperationException>(() => SchemaGenerator.Generate(jsonArray, "containerName"));
        }

        [TestMethod]
        public void TestJsonArrayWithNullElement()
        {
            JsonDocument jsonArray = JsonDocument.Parse(@"[{ ""name"": ""John"", ""age"": null }]");

            string gqlSchema = SchemaGenerator.Generate(jsonArray.RootElement.EnumerateArray().Select(item => item.Deserialize<JsonDocument>()).ToList(), "containerName");

            string expectedSchema = @"type ContainerName @model {
              name: String!,
              age: String!
            }";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
        }

        public static string RemoveSpacesAndNewLinesRegex(string input)
        {
            return Regex.Replace(input, @"\s+", "");
        }

        public static void AreEqualAfterCleanup(string expected, string actual)
        {
            Assert.AreEqual(RemoveSpacesAndNewLinesRegex(expected), RemoveSpacesAndNewLinesRegex(actual));
        }
    }
}
