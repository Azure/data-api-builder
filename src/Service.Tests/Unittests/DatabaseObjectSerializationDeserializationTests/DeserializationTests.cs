// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Services.MetadataProviders.Converters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass]
    [TestCategory("Serialization and Deserialization using SqlMetadataProvider converters")]
    public class DeserializationTests
    {
        [TestMethod]
        public void TestDatabaseTableDeserialization()
        {
             JsonSerializerOptions options = new()
             {
                Converters = {
                    new DatabaseObjectConverter(),
                    new TypeConverter(),
                    new ObjectConverter(),
                },
                ReferenceHandler = ReferenceHandler.Preserve,
                WriteIndented = true
            };

            string assemblyDirectory = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string serializedString = File.ReadAllText($"{assemblyDirectory}\\Unittests\\DatabaseObjectSerializationDeserializationTests\\DatabaseTableWithRelationships.txt");

            Dictionary<string, DatabaseObject> deserializedDictionary = JsonSerializer.Deserialize<Dictionary<string, DatabaseObject>>(serializedString, options)!;

            Assert.IsNotNull(deserializedDictionary);
        }
    }
}
