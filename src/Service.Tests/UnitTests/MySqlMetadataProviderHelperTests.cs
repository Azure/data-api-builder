// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlMetadataProviderHelperTests
    {
        [TestMethod]
        public void ParseSchemaAndDbTableName_RejectsExplicitSchema()
        {
            MySqlMetadataProvider provider = CreateProvider();

            Assert.ThrowsException<DataApiBuilderException>(
                () => provider.ParseSchemaAndDbTableName("custom.books"));
        }

        [TestMethod]
        public void SqlToCLRType_IsNotImplemented()
        {
            MySqlMetadataProvider provider = CreateProvider();

            Assert.ThrowsException<NotImplementedException>(() => provider.SqlToCLRType("int"));
        }

        private static MySqlMetadataProvider CreateProvider()
        {
            MySqlMetadataProvider provider =
                (MySqlMetadataProvider)RuntimeHelpers.GetUninitializedObject(typeof(MySqlMetadataProvider));
            SetBaseField(provider, "_databaseType", DatabaseType.MySQL);
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
