// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
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
        [DataRow("CosmosTests/TestData/Json")]
        public void TestSchemaGenerator(string filePath)
        {
            IDictionary<string, JObject> scenarios = new Dictionary<string, JObject>();

            string[] successPayloadFiles = Directory.GetFiles(filePath, "*.json");
            foreach (string payloadFile in successPayloadFiles)
            {
                string json = Regex.Replace(File.ReadAllText(payloadFile, Encoding.UTF8), @"\s+", string.Empty);
                
                string filename = Path.GetFileNameWithoutExtension(payloadFile);
                scenarios.Add(filename, JsonConvert.DeserializeObject<JObject>(json));
            }

            foreach (var item in scenarios)
            {
                JArray jsonArray = new (item.Value);
                // Act
                string schema = SchemaGenerator.Run(jsonArray, "containerName");
                //File.Create(@$"C:\Workspace\Azure\data-api-builder\src\Service.Tests\CosmosTests\TestData\Gql\/{item.Key}.gql");
                File.WriteAllText(@$"C:\Workspace\Azure\data-api-builder\src\Service.Tests\CosmosTests\TestData\Gql\/{item.Key}.gql", schema);
                Console.WriteLine(item.Key);
                Console.WriteLine(schema);
                Console.WriteLine("-------------");

                // Assert
                Assert.IsNotNull(schema);
            }
        }

        [TestMethod]
        public void TestSimpleJsonObject()
        {
            var jsonArray = JArray.Parse(@"[{ ""name"": ""John"", ""age"": 30, ""isStudent"": false }]");

            string gqlSchema = SchemaGenerator.Run(jsonArray, "containerName");

            string expectedSchema = @"type Containername @model {
  name: String
  age: Int
  isStudent: Boolean
}";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
        }

        [TestMethod]
        public void TestNestedJsonObject()
        {
            var jsonArray = JArray.Parse(@"[{ ""name"": ""John"", ""address"": { ""street"": ""123 Main St"", ""city"": ""Anytown"" } }]");

            string gqlSchema = SchemaGenerator.Run(jsonArray, "containerName");

            string expectedSchema = @"type Address {
  street: String
  city: String
}


type Containername @model {
  name: String
  address: Address
}";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
        }

        [TestMethod]
        public void TestJsonArray()
        {
            var jsonArray = JArray.Parse(@"[{ ""name"": ""John"", ""courses"": [ { ""name"": ""Math"" }, { ""name"": ""History"" } ] }]");

            string gqlSchema = SchemaGenerator.Run(jsonArray, "containerName");

            string expectedSchema = @"type CoursesItem {
  name: String
}


type Containername @model {
  name: String
  courses: [CoursesItem]
}";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
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
