// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class EntitySourceConverterTests
    {
        private static JsonSerializerOptions Options => RuntimeConfigLoader.GetSerializationOptions();

        [TestMethod]
        public void Deserialize_StringSource_CreatesTableSource()
        {
            EntitySource? source = JsonSerializer.Deserialize<EntitySource>("\"dbo.books\"", Options);

            Assert.IsNotNull(source);
            Assert.AreEqual("dbo.books", source.Object);
            Assert.AreEqual(EntitySourceType.Table, source.Type);
            Assert.AreEqual(0, source.Parameters?.Count);
            Assert.AreEqual(0, source.KeyFields?.Length);
        }

        [TestMethod]
        public void Deserialize_LegacyParameters_ConvertsClrValuesToStrings()
        {
            const string json = """
                {
                  "object": "dbo.run_report",
                  "type": "stored-procedure",
                  "parameters": {
                    "text": "value",
                    "integer": 42,
                    "decimal": 1.25,
                    "huge": 1e400,
                    "truth": true,
                    "falsehood": false,
                    "nothing": null,
                    "complex": { "x": 1 }
                  }
                }
                """;

            EntitySource? source = JsonSerializer.Deserialize<EntitySource>(json, Options);

            Assert.IsNotNull(source);
            Assert.IsNotNull(source.Parameters);
            Assert.AreEqual(8, source.Parameters.Count);
            Assert.AreEqual("value", FindDefault(source, "text"));
            Assert.AreEqual("42", FindDefault(source, "integer"));
            Assert.AreEqual("1.25", FindDefault(source, "decimal"));
            Assert.AreEqual(double.PositiveInfinity.ToString(), FindDefault(source, "huge"));
            Assert.AreEqual("True", FindDefault(source, "truth"));
            Assert.AreEqual("False", FindDefault(source, "falsehood"));
            Assert.AreEqual(string.Empty, FindDefault(source, "nothing"));
            Assert.AreEqual("{ \"x\": 1 }", FindDefault(source, "complex"));
        }

        [TestMethod]
        public void Deserialize_ModernParameters_PreservesList()
        {
            const string json = """
                {
                  "object": "dbo.run_report",
                  "type": "stored-procedure",
                  "parameters": [
                    { "name": "limit", "default": "10", "required": false }
                  ]
                }
                """;

            EntitySource? source = JsonSerializer.Deserialize<EntitySource>(json, Options);

            Assert.IsNotNull(source);
            Assert.IsNotNull(source.Parameters);
            Assert.AreEqual(1, source.Parameters.Count);
            Assert.AreEqual("limit", source.Parameters[0].Name);
            Assert.AreEqual("10", source.Parameters[0].Default);
        }

        [TestMethod]
        public void Serialize_RoundTripsObjectSource()
        {
            EntitySource source = new(
                "dbo.books",
                EntitySourceType.Table,
                Parameters: new(),
                KeyFields: new[] { "id" });

            string json = JsonSerializer.Serialize(source, Options);
            EntitySource? roundTripped = JsonSerializer.Deserialize<EntitySource>(json, Options);

            Assert.IsNotNull(roundTripped);
            Assert.AreEqual(source.Object, roundTripped.Object);
            Assert.AreEqual(source.Type, roundTripped.Type);
            CollectionAssert.AreEqual(source.KeyFields, roundTripped.KeyFields);
        }

        private static string? FindDefault(EntitySource source, string name)
        {
            return source.Parameters?.Find(parameter => parameter.Name == name)?.Default;
        }
    }
}
