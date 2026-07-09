// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Sql;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Sql
{
    /// <summary>
    /// Unit tests for <see cref="SchemaConverter.GetGraphQLTypeFromSystemType"/> covering
    /// scalar, array, and unsupported-type branches.
    /// </summary>
    [TestClass]
    public class SchemaConverterTypeMappingTests
    {
        [DataTestMethod]
        [DataRow(typeof(string), STRING_TYPE)]
        [DataRow(typeof(Guid), UUID_TYPE)]
        [DataRow(typeof(byte), BYTE_TYPE)]
        [DataRow(typeof(short), SHORT_TYPE)]
        [DataRow(typeof(int), INT_TYPE)]
        [DataRow(typeof(long), LONG_TYPE)]
        [DataRow(typeof(float), SINGLE_TYPE)]
        [DataRow(typeof(double), FLOAT_TYPE)]
        [DataRow(typeof(decimal), DECIMAL_TYPE)]
        [DataRow(typeof(bool), BOOLEAN_TYPE)]
        [DataRow(typeof(DateTime), DATETIME_TYPE)]
        [DataRow(typeof(DateTimeOffset), DATETIME_TYPE)]
        [DataRow(typeof(byte[]), BYTEARRAY_TYPE)]
        [DataRow(typeof(TimeOnly), LOCALTIME_TYPE)]
        [DataRow(typeof(TimeSpan), LOCALTIME_TYPE)]
        public void GetGraphQLTypeFromSystemType_ScalarTypes(Type systemType, string expected)
        {
            Assert.AreEqual(expected, SchemaConverter.GetGraphQLTypeFromSystemType(systemType));
        }

        [TestMethod]
        public void GetGraphQLTypeFromSystemType_ArrayType_ResolvesElementType()
        {
            // int[] resolves to its element type's GraphQL type.
            Assert.AreEqual(INT_TYPE, SchemaConverter.GetGraphQLTypeFromSystemType(typeof(int[])));
            Assert.AreEqual(STRING_TYPE, SchemaConverter.GetGraphQLTypeFromSystemType(typeof(string[])));
        }

        [TestMethod]
        public void GetGraphQLTypeFromSystemType_AbstractArray_DefaultsToString()
        {
            // Npgsql may report the abstract System.Array for unresolved array columns.
            Assert.AreEqual(STRING_TYPE, SchemaConverter.GetGraphQLTypeFromSystemType(typeof(Array)));
        }

        [TestMethod]
        public void GetGraphQLTypeFromSystemType_UnsupportedType_Throws()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => SchemaConverter.GetGraphQLTypeFromSystemType(typeof(object)));

            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.GraphQLMapping, ex.SubStatusCode);
        }

        [DataTestMethod]
        [DataRow((byte)5, BYTE_TYPE)]
        [DataRow((short)5, SHORT_TYPE)]
        [DataRow(5, INT_TYPE)]
        [DataRow(5L, LONG_TYPE)]
        [DataRow("text", STRING_TYPE)]
        [DataRow(true, BOOLEAN_TYPE)]
        [DataRow(1.5f, SINGLE_TYPE)]
        [DataRow(1.5d, FLOAT_TYPE)]
        public void CreateValueNodeFromDbObjectMetadata_ScalarTypes_WrapsInObjectFieldNode(object value, string expectedTypeName)
        {
            IValueNode node = SchemaConverter.CreateValueNodeFromDbObjectMetadata(value);

            ObjectValueNode objectValueNode = (ObjectValueNode)node;
            Assert.AreEqual(1, objectValueNode.Fields.Count);
            Assert.AreEqual(expectedTypeName, objectValueNode.Fields[0].Name.Value);
        }

        [TestMethod]
        public void CreateValueNodeFromDbObjectMetadata_UnsupportedType_Throws()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => SchemaConverter.CreateValueNodeFromDbObjectMetadata(new object()));

            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.GraphQLMapping, ex.SubStatusCode);
        }
    }
}
