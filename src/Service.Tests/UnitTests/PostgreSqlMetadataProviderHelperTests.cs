// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Data;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlMetadataProviderHelperTests
    {
        [TestMethod]
        public void ParseSchemaAndDbTableName_UsesSearchPathFromConnectionString()
        {
            PostgreSqlMetadataProvider provider = CreateProvider();
            SetBaseField(provider, "<ConnectionString>k__BackingField", "Host=localhost;Database=db;SearchPath=tenant");

            Assert.AreEqual(("tenant", "books"), provider.ParseSchemaAndDbTableName("books"));
        }

        [TestMethod]
        public void TryGetSchemaFromConnectionString_InvalidConnectionStringThrowsInitializationError()
        {
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(() =>
                PostgreSqlMetadataProvider.TryGetSchemaFromConnectionString("Host=localhost;Invalid Keyword=value", out _));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, exception.SubStatusCode);
        }

        [TestMethod]
        public void SqlToCLRType_IsNotImplemented()
        {
            PostgreSqlMetadataProvider provider = CreateProvider();

            Assert.ThrowsException<NotImplementedException>(() => provider.SqlToCLRType("integer"));
        }

        [TestMethod]
        public void PopulateColumnDefinitionWithHasDefaultAndDbType_MapsArrayUdtMetadata()
        {
            SourceDefinition definition = new();
            definition.Columns["numbers"] = new ColumnDefinition(typeof(Array));

            DataTable columns = new();
            columns.Columns.Add("COLUMN_NAME", typeof(string));
            columns.Columns.Add("COLUMN_DEFAULT", typeof(object));
            columns.Columns.Add("DATA_TYPE", typeof(string));
            columns.Columns.Add("UDT_NAME", typeof(string));
            columns.Rows.Add("numbers", DBNull.Value, "ARRAY", "_int4");
            columns.Rows.Add("not_configured", DBNull.Value, "ARRAY", "_text");

            PostgreSqlMetadataProvider provider = CreateProvider();
            typeof(PostgreSqlMetadataProvider).GetMethod(
                "PopulateColumnDefinitionWithHasDefaultAndDbType",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(provider, new object[] { definition, columns });

            ColumnDefinition numbers = definition.Columns["numbers"];
            Assert.IsFalse(numbers.HasDefault);
            Assert.IsTrue(numbers.IsArrayType);
            Assert.IsTrue(numbers.IsReadOnly);
            Assert.AreEqual(typeof(int), numbers.ElementSystemType);
            Assert.AreEqual(typeof(int[]), numbers.SystemType);
        }

        private static PostgreSqlMetadataProvider CreateProvider()
        {
            PostgreSqlMetadataProvider provider =
                (PostgreSqlMetadataProvider)RuntimeHelpers.GetUninitializedObject(typeof(PostgreSqlMetadataProvider));
            SetBaseField(provider, "_databaseType", DatabaseType.PostgreSQL);
            return provider;
        }

        private static void SetBaseField(object instance, string fieldName, object value)
        {
            Type? type = instance.GetType();
            while (type is not null)
            {
                FieldInfo? field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field is not null)
                {
                    field.SetValue(instance, value);
                    return;
                }

                type = type.BaseType;
            }

            Assert.Fail($"Unable to find field '{fieldName}'.");
        }
    }
}
