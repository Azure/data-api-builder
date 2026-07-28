// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Data;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.OpenAPI;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Language;
using Microsoft.OData.Edm;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="TypeHelper"/> type-mapping helpers.
    /// Pure logic; no database required.
    /// </summary>
    [TestClass]
    public class TypeHelperTests
    {
        [DataTestMethod]
        [DataRow(typeof(string), EdmPrimitiveTypeKind.String)]
        [DataRow(typeof(Guid), EdmPrimitiveTypeKind.Guid)]
        [DataRow(typeof(byte), EdmPrimitiveTypeKind.Byte)]
        [DataRow(typeof(short), EdmPrimitiveTypeKind.Int16)]
        [DataRow(typeof(int), EdmPrimitiveTypeKind.Int32)]
        [DataRow(typeof(long), EdmPrimitiveTypeKind.Int64)]
        [DataRow(typeof(float), EdmPrimitiveTypeKind.Single)]
        [DataRow(typeof(double), EdmPrimitiveTypeKind.Double)]
        [DataRow(typeof(decimal), EdmPrimitiveTypeKind.Decimal)]
        [DataRow(typeof(bool), EdmPrimitiveTypeKind.Boolean)]
        [DataRow(typeof(DateTime), EdmPrimitiveTypeKind.DateTimeOffset)]
        [DataRow(typeof(DateTimeOffset), EdmPrimitiveTypeKind.DateTimeOffset)]
        [DataRow(typeof(TimeOnly), EdmPrimitiveTypeKind.TimeOfDay)]
        [DataRow(typeof(TimeSpan), EdmPrimitiveTypeKind.TimeOfDay)]
        public void GetEdmPrimitiveTypeFromSystemType_MapsExpectedKind(Type systemType, EdmPrimitiveTypeKind expected)
        {
            Assert.AreEqual(expected, TypeHelper.GetEdmPrimitiveTypeFromSystemType(systemType));
        }

        [TestMethod]
        public void GetEdmPrimitiveTypeFromSystemType_ArrayType_UsesElementType()
        {
            Assert.AreEqual(EdmPrimitiveTypeKind.Int32, TypeHelper.GetEdmPrimitiveTypeFromSystemType(typeof(int[])));
        }

        [TestMethod]
        public void GetEdmPrimitiveTypeFromSystemType_AbstractArray_DefaultsToString()
        {
            Assert.AreEqual(EdmPrimitiveTypeKind.String, TypeHelper.GetEdmPrimitiveTypeFromSystemType(typeof(Array)));
        }

        [TestMethod]
        public void GetEdmPrimitiveTypeFromSystemType_UnsupportedType_Throws()
        {
            Assert.ThrowsException<ArgumentException>(
                () => TypeHelper.GetEdmPrimitiveTypeFromSystemType(typeof(System.Text.StringBuilder)));
        }

        [DataTestMethod]
        [DataRow(typeof(int), JsonDataType.Integer)]
        [DataRow(typeof(long), JsonDataType.Integer)]
        [DataRow(typeof(double), JsonDataType.Number)]
        [DataRow(typeof(decimal), JsonDataType.Number)]
        [DataRow(typeof(bool), JsonDataType.Boolean)]
        [DataRow(typeof(string), JsonDataType.String)]
        [DataRow(typeof(Guid), JsonDataType.String)]
        [DataRow(typeof(object), JsonDataType.Object)]
        [DataRow(typeof(int?), JsonDataType.Integer)]
        public void GetJsonDataTypeFromSystemType_MapsExpected(Type systemType, JsonDataType expected)
        {
            Assert.AreEqual(expected, TypeHelper.GetJsonDataTypeFromSystemType(systemType));
        }

        [TestMethod]
        public void GetJsonDataTypeFromSystemType_UnmappedType_ReturnsUndefined()
        {
            Assert.AreEqual(JsonDataType.Undefined, TypeHelper.GetJsonDataTypeFromSystemType(typeof(System.Text.StringBuilder)));
        }

        [DataTestMethod]
        [DataRow(typeof(int), DbType.Int32)]
        [DataRow(typeof(long), DbType.Int64)]
        [DataRow(typeof(string), DbType.String)]
        [DataRow(typeof(bool), DbType.Boolean)]
        [DataRow(typeof(Guid), DbType.Guid)]
        [DataRow(typeof(int?), DbType.Int32)]
        public void GetDbTypeFromSystemType_MapsExpected(Type systemType, DbType expected)
        {
            Assert.AreEqual(expected, TypeHelper.GetDbTypeFromSystemType(systemType));
        }

        [TestMethod]
        public void GetDbTypeFromSystemType_UnmappedType_ReturnsNull()
        {
            Assert.IsNull(TypeHelper.GetDbTypeFromSystemType(typeof(System.Text.StringBuilder)));
        }

        [DataTestMethod]
        [DataRow("bigint", typeof(long))]
        [DataRow("int", typeof(int))]
        [DataRow("bit", typeof(bool))]
        [DataRow("nvarchar(50)", typeof(string))]
        [DataRow("numeric", typeof(decimal))]
        [DataRow("uniqueidentifier", typeof(Guid))]
        public void GetSystemTypeFromSqlDbType_MapsExpected(string sqlDbTypeName, Type expected)
        {
            Assert.AreEqual(expected, TypeHelper.GetSystemTypeFromSqlDbType(sqlDbTypeName));
        }

        [TestMethod]
        public void GetSystemTypeFromSqlDbType_Unsupported_Throws()
        {
            Assert.ThrowsException<DataApiBuilderException>(
                () => TypeHelper.GetSystemTypeFromSqlDbType("notatype"));
        }

        [DataTestMethod]
        [DataRow(SqlDbType.Date, true, DbType.Date)]
        [DataRow(SqlDbType.DateTime2, true, DbType.DateTime2)]
        [DataRow(SqlDbType.DateTimeOffset, true, DbType.DateTimeOffset)]
        [DataRow(SqlDbType.Int, false, DbType.AnsiString)]
        public void TryGetDbTypeFromSqlDbDateTimeType_ReturnsExpected(SqlDbType sqlDbType, bool expectedFound, DbType expectedDbType)
        {
            bool found = TypeHelper.TryGetDbTypeFromSqlDbDateTimeType(sqlDbType, out DbType dbType);

            Assert.AreEqual(expectedFound, found);
            if (expectedFound)
            {
                Assert.AreEqual(expectedDbType, dbType);
            }
        }

        [TestMethod]
        public void GetValue_ConvertsValueNodesToClrValues()
        {
            Assert.AreEqual(5, TypeHelper.GetValue(new IntValueNode(5)));
            Assert.AreEqual(1.5d, TypeHelper.GetValue(new FloatValueNode(1.5d)));
            Assert.AreEqual(true, TypeHelper.GetValue(new BooleanValueNode(true)));
            Assert.AreEqual("hi", TypeHelper.GetValue(new StringValueNode("hi")));
            Assert.IsNull(TypeHelper.GetValue(NullValueNode.Default));
        }

        [DataTestMethod]
        [DataRow(SyntaxKind.IntValue, true)]
        [DataRow(SyntaxKind.FloatValue, true)]
        [DataRow(SyntaxKind.BooleanValue, true)]
        [DataRow(SyntaxKind.StringValue, true)]
        [DataRow(SyntaxKind.NullValue, true)]
        [DataRow(SyntaxKind.ListValue, false)]
        [DataRow(SyntaxKind.ObjectValue, false)]
        public void IsPrimitiveType_ReturnsExpected(SyntaxKind kind, bool expected)
        {
            Assert.AreEqual(expected, TypeHelper.IsPrimitiveType(kind));
        }
    }
}
