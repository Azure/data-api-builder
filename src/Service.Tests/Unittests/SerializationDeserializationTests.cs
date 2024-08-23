// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Services.MetadataProviders.Converters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass]
    [TestCategory("Serialization and Deserialization using SqlMetadataProvider converters")]
    public class SerializationDeserializationTests
    {
        private DatabaseTable _databaseTable;
        private DatabaseView _databaseView;
        private DatabaseStoredProcedure _databaseStoredProcedure;
        private ColumnDefinition _columnDefinition;
        private ParameterDefinition _parameterDefinition;
        private SourceDefinition _sourceDefinition;
        private JsonSerializerOptions _options;

        /// <summary>
        /// Validates serialization and deserilization of DatabaseTable object
        /// This test first creates a DatabaseTable object, serializes and deserializes it using our converters
        /// and verifies that the deserialized object is same as original.
        /// For DatabaseTable we are checking properties : SchemaName, Name, FullName, SourceType, SourceDefinition and TableDefinition
        /// This test also tests number of properties currently on DatabaseTable object, this is for future scenario, if there is a new
        /// property added this test will catch it and also will expose developer to the serialization and deserialization logic
        /// </summary>
        [TestMethod]
        public void TestDatabaseTableSerializationDeserialization()
        {
            InitializeObjects();

            TestTypeNameChanges(_databaseTable, "DatabaseTable");

            // Test to catch if there is change in number of properties/fields
            // Note: On Addition of property make sure it is added in following object creation _databaseTable and include in serialization
            // and deserialization test.
            int fields = typeof(DatabaseTable).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Length;
            Assert.AreEqual(fields, 6);

            string serializedDatabaseTable = JsonSerializer.Serialize(_databaseTable, _options);
            DatabaseTable deserializedDatabaseTable = JsonSerializer.Deserialize<DatabaseTable>(serializedDatabaseTable, _options)!;

            Assert.AreEqual(deserializedDatabaseTable.SourceType, _databaseTable.SourceType);
            Assert.AreEqual(deserializedDatabaseTable.FullName, _databaseTable.FullName);
            deserializedDatabaseTable.Equals(_databaseTable);
            VerifySourceDefinitionSerializationDeserialization(deserializedDatabaseTable.SourceDefinition, _databaseTable.SourceDefinition, "FirstName");
            VerifySourceDefinitionSerializationDeserialization(deserializedDatabaseTable.TableDefinition, _databaseTable.TableDefinition, "FirstName");
        }

        /// <summary>
        /// Validates serialization and deserilization of DatabaseView object
        /// This test first creates a DatabaseTable object, serializes and deserializes it using our converters
        /// and verifies that the deserialized object is same as original.
        /// For DatabaseView we are checking properties : SchemaName, Name, FullName, SourceType, SourceDefinition and ViewDefinition
        /// This test also tests number of properties currently on DatabaseTable object, this is for future scenario, if there is a new
        /// property added this test will catch it and also will expose developer to the serialization and deserialization logic
        /// </summary>
        [TestMethod]
        public void TestDatabaseViewSerializationDeserialization()
        {
            InitializeObjects();

            TestTypeNameChanges(_databaseView, "DatabaseView");

            // Test to catch if there is change in number of properties/fields
            // Note: On Addition of property make sure it is added in following object creation _databaseView and include in serialization
            // and deserialization test.
            int fields = typeof(DatabaseView).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Length;
            Assert.AreEqual(fields, 6);

            string serializedDatabaseView = JsonSerializer.Serialize(_databaseView, _options);
            DatabaseView deserializedDatabaseView = JsonSerializer.Deserialize<DatabaseView>(serializedDatabaseView, _options)!;

            Assert.AreEqual(deserializedDatabaseView.SourceType, _databaseView.SourceType);
            deserializedDatabaseView.Equals(_databaseView);
            VerifySourceDefinitionSerializationDeserialization(deserializedDatabaseView.SourceDefinition, _databaseView.SourceDefinition, "FirstName");
            VerifySourceDefinitionSerializationDeserialization(deserializedDatabaseView.ViewDefinition, _databaseView.ViewDefinition, "FirstName");
        }

        /// <summary>
        /// Validates serialization and deserilization of DatabaseStoredProcedure object
        /// This test first creates a DatabaseTable object, serializes and deserializes it using our converters
        /// and verifies that the deserialized object is same as original.
        /// For DatabaseStoredProcedure we are checking properties : SchemaName, Name, FullName, SourceType, SourceDefinition and StoredProcedureDefinition
        /// This test also tests number of properties currently on DatabaseTable object, this is for future scenario, if there is a new
        /// property added this test will catch it and also will expose developer to the serialization and deserialization logic
        /// </summary>
        [TestMethod]
        public void TestDatabaseStoredProcedureSerializationDeserialization()
        {
            InitializeObjects();

            TestTypeNameChanges(_databaseStoredProcedure, "DatabaseStoredProcedure");

            // Test to catch if there is change in number of properties/fields
            // Note: On Addition of property make sure it is added in following object creation _databaseStoredProcedure and include in serialization
            // and deserialization test.
            int fields = typeof(DatabaseStoredProcedure).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Length;
            Assert.AreEqual(fields, 6);

            string serializedDatabaseSP = JsonSerializer.Serialize(_databaseStoredProcedure, _options);
            DatabaseStoredProcedure deserializedDatabaseSP = JsonSerializer.Deserialize<DatabaseStoredProcedure>(serializedDatabaseSP, _options)!;

            Assert.AreEqual(deserializedDatabaseSP.SourceType, _databaseStoredProcedure.SourceType);
            deserializedDatabaseSP.Equals(_databaseStoredProcedure);
            VerifySourceDefinitionSerializationDeserialization(deserializedDatabaseSP.SourceDefinition, _databaseStoredProcedure.SourceDefinition, "FirstName", true);
            VerifySourceDefinitionSerializationDeserialization(deserializedDatabaseSP.StoredProcedureDefinition, _databaseStoredProcedure.StoredProcedureDefinition, "FirstName", true);
        }

        /// <summary>
        /// Validates serialization and deserilization of RelationShipPair object
        /// This test first creates a RelationshipPair object, serializes and deserializes it using our converters
        /// and verifies that the deserialized object is same as original.
        /// </summary>
        [TestMethod]
        public void TestRelationShipPairSerializationDeserialization()
        {
            InitializeObjects();

            RelationShipPair pair = GetRelationShipPair();
            string serializedRelationShipPair = JsonSerializer.Serialize(pair, _options);
            RelationShipPair deserializedRelationShipPair = JsonSerializer.Deserialize<RelationShipPair>(serializedRelationShipPair, _options);

            VerifyRelationShipPair(pair, deserializedRelationShipPair);
        }

        /// <summary>
        /// Validates serialization and deserilization of ForeignKeyDefinition object
        /// </summary>
        [TestMethod]
        public void TestForeignKeyDefinitionSerializationDeserialization()
        {
            InitializeObjects();

            RelationShipPair pair = GetRelationShipPair();

            ForeignKeyDefinition foreignKeyDefinition = new()
            {
                Pair = pair,
                ReferencedColumns = new List<string> { "Index" },
                ReferencingColumns = new List<string> { "FirstName" }
            };

            string serializedForeignKeyDefinition = JsonSerializer.Serialize(foreignKeyDefinition, _options);
            ForeignKeyDefinition deserializedForeignKeyDefinition = JsonSerializer.Deserialize<ForeignKeyDefinition>(serializedForeignKeyDefinition, _options);

            List<FieldInfo> fieldMetadata = typeof(ForeignKeyDefinition).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).ToList();
            Assert.AreEqual(expected: 7, actual: fieldMetadata.Count);

            Assert.IsTrue(foreignKeyDefinition.Equals(deserializedForeignKeyDefinition));
            VerifyRelationShipPair(pair, deserializedForeignKeyDefinition.Pair);
        }

        /// <summary>
        /// Validates serialization and deserilization of SourceDefinition object with SourceEntityRelationShipMap property
        /// SourceDefinition -> RelationShipMetadata -> ForeignKeyDefinition -> RelationshipPair -> DatabaseTable -> SourceDefinition this creates a cycle between the objects
        /// to handle this we need serialization property : ReferenceHandler.Preserve
        /// </summary>
        [TestMethod]
        public void TestSourceDefinitionCyclicObjectsSerializationDeserialization()
        {
            InitializeObjects();

            RelationShipPair pair = GetRelationShipPair();

            ForeignKeyDefinition foreignKeyDefinition = new()
            {
                Pair = pair,
                ReferencedColumns = new List<string> { "Index" },
                ReferencingColumns = new List<string> { "FirstName" }

            };

            RelationshipMetadata metadata = new();
            metadata.TargetEntityToFkDefinitionMap.Add("customers", new List<ForeignKeyDefinition> { foreignKeyDefinition });

            _sourceDefinition.SourceEntityRelationshipMap.Add("persons", metadata);

            // In serialization options we need  ReferenceHandler = ReferenceHandler.Preserve, or else it doesnot seialize objects with cycle references
            // SourceDefinition -> RelationShipMetadata -> ForeignKeyDefinition RelationshipPair ->DatabaseTable -> SourceDefinition
            Assert.ThrowsException<JsonException>(() =>
            {
                JsonSerializer.Serialize(_sourceDefinition, _options);
            });

            // below code tests serialization and deserialization at different object levels inside the source definition cycle
            _options = new()
            {
                Converters = {
                    new DatabaseObjectConverter(),
                    new TypeConverter(),
                    new ObjectConverter(),
                },
                ReferenceHandler = ReferenceHandler.Preserve,
            };

            // Verify SourceDefinition except relationshipmetadata property
            string serializedSourceDefinition = JsonSerializer.Serialize(_sourceDefinition, _options);
            SourceDefinition deserializedSourceDefinition = JsonSerializer.Deserialize<SourceDefinition>(serializedSourceDefinition, _options);
            VerifySourceDefinitionSerializationDeserialization(_sourceDefinition, deserializedSourceDefinition, "FirstName");

            // verify ForeignKeyDefinition
            ForeignKeyDefinition expectedForeignKeyDefinition = _sourceDefinition.SourceEntityRelationshipMap["persons"].TargetEntityToFkDefinitionMap["customers"][0];
            ForeignKeyDefinition currentForeignKeyDefinition = deserializedSourceDefinition.SourceEntityRelationshipMap["persons"].TargetEntityToFkDefinitionMap["customers"][0];

            // verify RelationShip Pair
            Assert.IsTrue(expectedForeignKeyDefinition.Equals(currentForeignKeyDefinition));
            VerifyRelationShipPair(expectedForeignKeyDefinition.Pair, currentForeignKeyDefinition.Pair);
        }

        [TestMethod]
        public void TestColumnDefinitionNegativeCases()
        {
            InitializeObjects();

            ColumnDefinition col2 = GetColumnDefinition(typeof(string), null, true, false, false, new string("John"), false);
            // as DbType of one is null and other is not it will fail - testing DbType?.Equals(other.DbType) == true to return false
            Assert.IsFalse(col2.Equals(_columnDefinition));

            ColumnDefinition col3 = GetColumnDefinition(typeof(string), null, false, false, false, null, false);
            // as DefaultValue of one is null and other is not it will fail - testing DefaultValue?.Equals(other.DefaultValue) == true to return false
            Assert.IsFalse(col3.Equals(_columnDefinition));

            _options = new JsonSerializerOptions()
            {
                Converters = { new ObjectConverter() }
            };

            // test to check if TypeConverter is not passed then we cannot serialize System.Type
            Assert.ThrowsException<NotSupportedException>(() =>
            {
                JsonSerializer.Serialize(_columnDefinition, _options);
            });

            // test to check the need for ObjectConverter to handle object DefaultValue of type object
            _options = new JsonSerializerOptions()
            {
                Converters = { new TypeConverter() }
            };
            string serializeColumnDefinition = JsonSerializer.Serialize(_columnDefinition, _options);
            ColumnDefinition col = JsonSerializer.Deserialize<ColumnDefinition>(serializeColumnDefinition, _options);
            Assert.IsFalse(_columnDefinition.Equals(col));
        }

        /// <summary>
        /// Validates serialization and deserilization of Dictionary containing DatabaseTable
        /// this is how we serialize and deserialize metadataprovider.EntityToDatabaseObject dict.
        /// Temporarily ignore test for .net6 due to npgsql issue.
        /// </summary>
        [TestMethod]
        public void TestDictionaryDatabaseObjectSerializationDeserialization()
        {
            InitializeObjects();

            Dictionary<string, DatabaseObject> dict = new() { { "person", _databaseTable } };

            string serializedDict = JsonSerializer.Serialize(dict, _options);
            Dictionary<string, DatabaseObject> deserializedDict = JsonSerializer.Deserialize<Dictionary<string, DatabaseObject>>(serializedDict, _options)!;

            DatabaseTable deserializedDatabaseTable = (DatabaseTable)deserializedDict["person"];

            Assert.AreEqual(deserializedDatabaseTable.SourceType, _databaseTable.SourceType);
            Assert.AreEqual(deserializedDatabaseTable.FullName, _databaseTable.FullName);
            deserializedDatabaseTable.Equals(_databaseTable);
            VerifySourceDefinitionSerializationDeserialization(deserializedDatabaseTable.SourceDefinition, _databaseTable.SourceDefinition, "FirstName");
            VerifySourceDefinitionSerializationDeserialization(deserializedDatabaseTable.TableDefinition, _databaseTable.TableDefinition, "FirstName");
        }

        private void InitializeObjects()
        {
            _options = new()
            {
#if NET8_0_OR_GREATER
                // ObjectConverter behavior different in .NET8 most likely due to
                // .NET7 breaking change:
                // - https://learn.microsoft.com/dotnet/core/compatibility/serialization/7.0/polymorphic-serialization#affected-apis
                // Removing from converter list here does not negatively affect tests
                // though we are still looking into whether a better solution exists.
                // Preserving .NET6 behavior requires that Microsoft.Extensions.Configuration.Json dependency
                // version align to .NET runtime version. eg. dependency version 6.Y.Z for .NET6 and 8.Y.Z for .NET8
                Converters = {
                    new DatabaseObjectConverter(),
                    new TypeConverter()
                }
#else
                Converters = {
                    new DatabaseObjectConverter(),
                    new TypeConverter(),
                    new ObjectConverter()
                }
#endif
            };

            _columnDefinition = GetColumnDefinition(typeof(string), DbType.String, true, false, false, new string("John"), false);
            _sourceDefinition = GetSourceDefinition(false, false, new List<string>() { "FirstName" }, _columnDefinition);

            _databaseTable = new DatabaseTable()
            {
                Name = "customers",
                SourceType = EntitySourceType.Table,
                SchemaName = "model",
                TableDefinition = _sourceDefinition,
            };

            _databaseView = new DatabaseView()
            {
                Name = "customers",
                SourceType = EntitySourceType.View,
                SchemaName = "model",
                ViewDefinition = new()
                {
                    IsInsertDMLTriggerEnabled = false,
                    IsUpdateDMLTriggerEnabled = false,
                    PrimaryKey = new List<string>() { "FirstName" },
                },
            };
            _databaseView.ViewDefinition.Columns.Add("FirstName", _columnDefinition);

            _parameterDefinition = new()
            {
                SystemType = typeof(int),
                DbType = DbType.Int32,
                HasConfigDefault = true,
                ConfigDefaultValue = 1,
            };

            _databaseStoredProcedure = new DatabaseStoredProcedure()
            {
                SchemaName = "dbo",
                Name = "GetPersonById",
                SourceType = EntitySourceType.StoredProcedure,
                StoredProcedureDefinition = new()
                {
                    PrimaryKey = new List<string>() { "FirstName" },
                }
            };
            _databaseStoredProcedure.StoredProcedureDefinition.Columns.Add("FirstName", _columnDefinition);
            _databaseStoredProcedure.StoredProcedureDefinition.Parameters.Add("Id", _parameterDefinition);
        }

        /// <summary>
        /// During serialization we add TypeName to the DatabaseObject based on its type. TypeName is the AssemblyQualifiedName for the type.
        /// Example : "TypeName": "Azure.DataApiBuilder.Config.DatabasePrimitives.DatabaseTable, Azure.DataApiBuilder.Config",
        /// If there is a code refactor which changes the path of this object, the deserialization with old value will fail, as it wont be able to find the object in the location
        /// previously defined.
        /// Note : Currently this is being used by GraphQL workload, failure of this test case needs to be
        /// handled by opening a GitHub issue and tag as "GraphQLWorkloadBreakingChange" by external contributors. For internal contributors
        /// reach out to the GraphQL workload team.
        /// </summary>
        private void TestTypeNameChanges(DatabaseObject databaseobject, string objectName)
        {
            // This test checks the current code path of the object : DatabaseTable, DatabaseView or DatabaseStoredProcedure
            // It fetches the TypeName property from the serialized object and check if the TypeName contains the following project path
            Dictionary<string, DatabaseObject> dict = new();
            dict.Add("person", databaseobject);
            string jsonString = JsonSerializer.Serialize(dict, _options);

            // Find the start index of TypeName
            int typeNameIndex = jsonString.IndexOf("\"TypeName\":\"", StringComparison.Ordinal);
            int typeNameKeywordLength = 11;

            if (typeNameIndex != -1)
            {
                // Find the end index of TypeName
                int endIndex = jsonString.IndexOf("\",", typeNameIndex + typeNameKeywordLength, StringComparison.Ordinal);

                if (endIndex != -1)
                {
                    // Extract TypeName
                    string typeName = jsonString.Substring(typeNameIndex + typeNameKeywordLength, endIndex - typeNameIndex - typeNameKeywordLength).TrimStart('\"');

                    // Split TypeName into different parts
                    // following splits different sections of :
                    // "Azure.DataApiBuilder.Config.DatabasePrimitives.DatabaseTable, Azure.DataApiBuilder.Config"
                    string[] typeNameSplitParts = typeName.Split(',');

                    string namespaceString = typeNameSplitParts[0].Trim();
                    Assert.IsTrue(namespaceString.Contains("Azure.DataApiBuilder.Config.DatabasePrimitives"));
                    Assert.AreEqual(namespaceString, "Azure.DataApiBuilder.Config.DatabasePrimitives." + objectName);

                    string projectNameString = typeNameSplitParts[1].Trim();
                    Assert.AreEqual(projectNameString, "Azure.DataApiBuilder.Config");

                    Assert.AreEqual(typeNameSplitParts.Length, 2);
                }
                else
                {
                    Assert.Fail("Error in Serialization");
                }
            }
            else
            {
                Assert.Fail("Error in Serialization : TypeName substring not found");
            }
        }

        private static void VerifySourceDefinitionSerializationDeserialization(SourceDefinition expectedSourceDefinition, SourceDefinition deserializedSourceDefinition, string columnValue, bool isStoredProcedure = false)
        {
            // test number of properties/fields defined in Source Definition
            int fields = typeof(SourceDefinition).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Length;
            Assert.AreEqual(fields, 5);

            // test values
            Assert.AreEqual(expectedSourceDefinition.IsInsertDMLTriggerEnabled, deserializedSourceDefinition.IsInsertDMLTriggerEnabled);
            Assert.AreEqual(expectedSourceDefinition.IsUpdateDMLTriggerEnabled, deserializedSourceDefinition.IsUpdateDMLTriggerEnabled);
            Assert.AreEqual(expectedSourceDefinition.PrimaryKey.Count, deserializedSourceDefinition.PrimaryKey.Count);
            Assert.AreEqual(expectedSourceDefinition.Columns.Count, deserializedSourceDefinition.Columns.Count);
            VerifyColumnDefinitionSerializationDeserialization(expectedSourceDefinition.Columns.GetValueOrDefault(columnValue), deserializedSourceDefinition.Columns.GetValueOrDefault(columnValue));

            if (isStoredProcedure)
            {
                VerifyParameterDefinitionSerializationDeserialization(((StoredProcedureDefinition)expectedSourceDefinition).Parameters.GetValueOrDefault("Id"),
                    ((StoredProcedureDefinition)deserializedSourceDefinition).Parameters.GetValueOrDefault("Id"));
            }
        }

        private static void VerifyColumnDefinitionSerializationDeserialization(ColumnDefinition expectedColumnDefinition, ColumnDefinition deserializedColumnDefinition)
        {
            // test number of properties/fields defined in Column Definition
            int fields = typeof(ColumnDefinition).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Length;
            Assert.AreEqual(fields, 8);

            // test values
            expectedColumnDefinition.Equals(deserializedColumnDefinition);
        }

        private static void VerifyParameterDefinitionSerializationDeserialization(ParameterDefinition expectedParameterDefinition, ParameterDefinition deserializedParameterDefinition)
        {
            // test number of properties/fields defined in Column Definition
            int fields = typeof(ParameterDefinition).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Length;
            Assert.AreEqual(fields, 5);
            // test values
            expectedParameterDefinition.Equals(deserializedParameterDefinition);
        }

        private static void VerifyRelationShipPair(RelationShipPair expectedRelationShipPair, RelationShipPair deserializedRelationShipPair)
        {
            List<FieldInfo> fieldMetadata = typeof(RelationShipPair).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).ToList();
            Assert.AreEqual(expected: 3, actual: fieldMetadata.Count, message: $"Unexpected field count for object type {typeof(RelationShipPair)}");

            Assert.IsTrue(expectedRelationShipPair.Equals(deserializedRelationShipPair));

            // test referencingtable sourcedefinition and tabledefinition
            VerifySourceDefinitionSerializationDeserialization(deserializedRelationShipPair.ReferencingDbTable.SourceDefinition, expectedRelationShipPair.ReferencingDbTable.SourceDefinition, "FirstName");
            VerifySourceDefinitionSerializationDeserialization(deserializedRelationShipPair.ReferencingDbTable.TableDefinition, expectedRelationShipPair.ReferencingDbTable.TableDefinition, "FirstName");

            // test referenced table sourcedefinition and tabledefinition
            VerifySourceDefinitionSerializationDeserialization(deserializedRelationShipPair.ReferencedDbTable.SourceDefinition, expectedRelationShipPair.ReferencedDbTable.SourceDefinition, "Index");
            VerifySourceDefinitionSerializationDeserialization(deserializedRelationShipPair.ReferencedDbTable.TableDefinition, expectedRelationShipPair.ReferencedDbTable.TableDefinition, "Index");
        }

        private static ColumnDefinition GetColumnDefinition(Type SystemType, DbType? DbType, bool HasDefault, bool IsAutoGenerated, bool IsReadOnly, object DefaultVault, bool IsNullable)
        {
            return new()
            {
                SystemType = SystemType,
                DbType = DbType,
                HasDefault = HasDefault,
                IsAutoGenerated = IsAutoGenerated,
                IsReadOnly = IsReadOnly,
                DefaultValue = DefaultVault,
                IsNullable = IsNullable,
            };
        }

        private static SourceDefinition GetSourceDefinition(bool IsInsertDMLTriggerEnabled, bool IsUpdateDMLTriggerEnabled, List<string> PrimaryKeys, ColumnDefinition columnDefinition)
        {
            SourceDefinition sourceDefinition = new()
            {
                IsInsertDMLTriggerEnabled = IsInsertDMLTriggerEnabled,
                IsUpdateDMLTriggerEnabled = IsUpdateDMLTriggerEnabled,
                PrimaryKey = PrimaryKeys,
            };
            sourceDefinition.Columns.Add(PrimaryKeys[0], columnDefinition);

            return sourceDefinition;
        }

        private RelationShipPair GetRelationShipPair()
        {
            ColumnDefinition col2 = GetColumnDefinition(typeof(int), DbType.Int32, true, false, false, 10, false);
            SourceDefinition source2 = GetSourceDefinition(false, false, new List<string>() { "Index" }, col2);
            DatabaseTable table2 = new()
            {
                Name = "person",
                SourceType = EntitySourceType.Table,
                SchemaName = "model",
                TableDefinition = source2,
            };
            return new(_databaseTable, table2);
        }
    }
}
