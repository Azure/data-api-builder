// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Text.RegularExpressions;
using System.Text;
using Azure.DataApiBuilder.Core.Generator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class SchemaGeneratorTest
    {
        [TestMethod]
        [DataRow("CosmosTests/TestData/Json", "CosmosTests/TestData/Gql")]
        public void TestSchemaGenerator(string jsonFilePath, string gqlFilePath)
        {
            string[] successPayloadFiles = Directory.GetFiles(jsonFilePath, "*.json");
            foreach (string payloadFile in successPayloadFiles)
            {
                string json = Regex.Replace(File.ReadAllText(payloadFile, Encoding.UTF8), @"\s+", string.Empty);
                List<JObject> jsonArray = new () { JsonConvert.DeserializeObject<JObject>(json) };

                string actualSchema = SchemaGenerator.Generate(jsonArray, "containerName");

                string filename = Path.GetFileNameWithoutExtension(payloadFile);
                string expectedSchema = File.ReadAllText($"{gqlFilePath}/{filename}.gql");

                AreEqualAfterCleanup(expectedSchema, actualSchema);
            }
        }

        [TestMethod]
        [DataRow("CosmosTests/TestData/Json/MultiItems", "CosmosTests/TestData/Gql")]
        public void TestSchemaGeneratorUsingMultipleJson(string jsonFilePath, string gqlFilePath)
        {
            List<JObject> jArray = new ();

            string[] successPayloadFiles = Directory.GetFiles(jsonFilePath, "*.json");
            foreach (string payloadFile in successPayloadFiles)
            {
                string json = Regex.Replace(File.ReadAllText(payloadFile, Encoding.UTF8), @"\s+", string.Empty);
                jArray.Add(JsonConvert.DeserializeObject<JObject>(json));
            }

            string actualSchema = SchemaGenerator.Generate(jArray, "containerName");
            string expectedSchema = File.ReadAllText($"{gqlFilePath}/MultiItems.gql");

            AreEqualAfterCleanup(expectedSchema, actualSchema);
        }

        [TestMethod]
        public void TestMixedJsonArray()
        {
            List<JObject> jsonArray = new() {
                JObject.Parse(@"{ ""name"": ""John"", ""age"": 30, ""isStudent"": false, ""birthDate"": ""1980-01-01T00:00:00Z"" }"),
                JObject.Parse(@"{ ""email"": ""john@example.com"", ""phone"": ""123-456-7890"" }")};

            string gqlSchema = SchemaGenerator.Generate(jsonArray, "containerName");

            string expectedSchema = @"type ContainerName @model {
              name: String!
              age: Int!
              isStudent: Boolean!
              birthDate: String!
              email: String!
              phone: String!
            }";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
        }

        [TestMethod]
        public void TestEmptyJsonArray()
        {
            List<JObject> jsonArray = new ();
            Assert.ThrowsException<InvalidOperationException>(() => SchemaGenerator.Generate(jsonArray, "containerName"));
        }

        [TestMethod]
        public void TestJsonArrayWithNullElement()
        {
            JArray jsonArray = JArray.Parse(@"[{ ""name"": ""John"", ""age"": null }]");

            string gqlSchema = SchemaGenerator.Generate(jsonArray.Select(item => (JObject)item).ToList(), "containerName");

            string expectedSchema = @"type ContainerName @model {
              name: String
              age: String
            }";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
        }

        public static string RemoveSpacesAndNewLinesRegex(string input)
        {
            return Regex.Replace(input, @"\s+", "");
        }

        public static void AreEqualAfterCleanup(string actual, string expected)
        {
           Assert.AreEqual(RemoveSpacesAndNewLinesRegex(expected), RemoveSpacesAndNewLinesRegex(actual));
        }
    }
}
