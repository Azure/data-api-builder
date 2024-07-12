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
                
                string filename = Path.GetFileNameWithoutExtension(payloadFile);

                JArray jsonArray = new(JsonConvert.DeserializeObject<JObject>(json));
                string schema = SchemaGenerator.Run(jsonArray, "containerName");

                File.WriteAllText(@$"C:\Workspace\Azure\data-api-builder\src\Service.Tests\CosmosTests\TestData\Gql\/{filename}.gql", schema);
            }
        }

        [TestMethod]
        [DataRow("CosmosTests/TestData/Json/MultiItems", "CosmosTests/TestData/Gql")]
        public void TestSchemaGeneratorUsingMultipleJson(string jsonFilePath, string gqlFilePath)
        {
            JArray jArray = new ();

            string[] successPayloadFiles = Directory.GetFiles(jsonFilePath, "*.json");
            foreach (string payloadFile in successPayloadFiles)
            {
                string json = Regex.Replace(File.ReadAllText(payloadFile, Encoding.UTF8), @"\s+", string.Empty);
                jArray.Add(JsonConvert.DeserializeObject<JObject>(json));
            }

            // Act
            string schema = SchemaGenerator.Run(jArray, "containerName");

            //File.Create(@$"C:\Workspace\Azure\data-api-builder\src\Service.Tests\CosmosTests\TestData\Gql\/{item.Key}.gql");
            File.WriteAllText(@$"C:\Workspace\Azure\data-api-builder\src\Service.Tests\CosmosTests\TestData\Gql\/MultiItems.gql", schema);
        }

        [TestMethod]
        public void TestMixedJsonArray()
        {
            var jsonArray = JArray.Parse(@"[
            { ""name"": ""John"", ""age"": 30, ""isStudent"": false, ""birthDate"": ""1980-01-01T00:00:00Z"" },
            { ""email"": ""john@example.com"", ""phone"": ""123-456-7890"" }
        ]");

            string gqlSchema = SchemaGenerator.Run(jsonArray, "containerName");

            string expectedSchema = @"type Containername @model {
  name: String
  age: Int
  isStudent: Boolean
  birthDate: String
  email: String
  phone: String
}";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
        }

        [TestMethod]
        public void TestEmptyJsonArray()
        {
            var jsonArray = new JArray();

            Assert.ThrowsException<InvalidOperationException>(() => SchemaGenerator.Run(jsonArray, "containerName"));
        }

        [TestMethod]
        public void TestJsonArrayWithNonObjectElements()
        {
            var jsonArray = JArray.Parse(@"[1, 2, 3]");

            Assert.ThrowsException<InvalidOperationException>(() => SchemaGenerator.Run(jsonArray, "containerName"));
        }

        [TestMethod]
        public void TestJsonArrayWithNullElement()
        {
            var jsonArray = JArray.Parse(@"[{ ""name"": ""John"", ""age"": null }]");

            string gqlSchema = SchemaGenerator.Run(jsonArray, "containerName");

            string expectedSchema = @"type Containername @model {
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
