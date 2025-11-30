// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.OpenAPI;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OpenApiIntegration;

/// <summary>
/// Validates TypeHelper converters return expected results.
/// </summary>
[TestClass]
public class CLRtoJsonValueTypeUnitTests
{
    private const string STRING_PARSE_ERROR_PREFIX = "The input string value ";
    private const string ERROR_PREFIX = "The SqlDbType ";
    private const string SQLDBTYPE_RESOLUTION_ERROR = "failed to resolve to SqlDbType.";
    private const string SQLDBTYPE_UNEXPECTED_RESOLUTION_ERROR = "should have resolved to a SqlDbType.";
    private const string JSONDATATYPE_RESOLUTION_ERROR = "(when supported) should map to a system type and associated JsonDataType.";
    private const string DBTYPE_RESOLUTION_ERROR = "(when supported) should map to a system type and associated DbType.";

    /// <summary>
    /// Validates that:
    /// 1. String representations of SqlDbType provided by SQL Server/Azure SQL DB resolve to a SqlDbType enum
    /// and CLR/system type.
    /// 2. The resolved CLR/system types map to a defined JsonDataType.
    /// A DAB supported CLR type is a CLR type mapped from a database value type.
    /// </summary>
    /// <param name="sqlDataTypeLiteral">Raw string provided by database e.g. 'bigint'</param>
    /// <param name="isSupportedSqlDataType">Whether DAB supports the resolved SqlDbType value.</param>
    [TestMethod]
    [DataRow("UnsupportedTypeName", false, DisplayName = "Validate unexpected SqlDbType name value is handled gracefully.")]
    [DynamicData(nameof(GetTestData_SupportedSystemTypesMapToJsonValueType), DynamicDataSourceType.Method)]
    public void SupportedSystemTypesMapToJsonValueType(string sqlDataTypeLiteral, bool isSupportedSqlDataType)
    {
        try
        {
            Type resolvedType = TypeHelper.GetSystemTypeFromSqlDbType(sqlDataTypeLiteral);
            Assert.IsTrue(isSupportedSqlDataType, STRING_PARSE_ERROR_PREFIX + $"{{{sqlDataTypeLiteral}}} " + SQLDBTYPE_RESOLUTION_ERROR);

            JsonDataType resolvedJsonType = TypeHelper.GetJsonDataTypeFromSystemType(resolvedType);
            Assert.AreEqual(isSupportedSqlDataType, resolvedJsonType != JsonDataType.Undefined, ERROR_PREFIX + $"{{{sqlDataTypeLiteral}}} " + JSONDATATYPE_RESOLUTION_ERROR);
        }
        catch (DataApiBuilderException)
        {
            Assert.IsFalse(isSupportedSqlDataType, ERROR_PREFIX + $"{{{sqlDataTypeLiteral}}} " + SQLDBTYPE_UNEXPECTED_RESOLUTION_ERROR);
        }
    }

    /// <summary>
    /// Generates test cases for use in DynamicData method
    /// SupportedSystemTypesMapToJsonValueType
    /// Test cases will be named like: SupportedSystemTypesMapToJsonValueType (date,True)
    /// where 'date' is pair.key and 'True' is pair.Value from the source dictionary.
    /// </summary>
    /// <returns>Enumerator over object arrays with test case input data.</returns>
    /// <seealso cref="https://learn.microsoft.com/visualstudio/test/how-to-create-a-data-driven-unit-test?view=vs-2022#member-data-driven-test"/>
    private static IEnumerable<object[]> GetTestData_SupportedSystemTypesMapToJsonValueType()
    {
        List<object[]> testCases = new();

        foreach (KeyValuePair<string, bool> pair in SqlTypeConstants.SupportedSqlDbTypes)
        {
            testCases.Add(new object[] { pair.Key, pair.Value });
        }

        return testCases;
    }

    /// <summary>
    /// Validates the behavior of TypeHelper.GetJsonDataTypeFromSystemType(Type type) by
    /// ensuring that a nullable value type like int? is resolved to its underlying type int.
    /// Consequently, the lookup in the _systemTypeToJsonDataTypeMap and _systemTypeToDbTypeMap
    /// dictionary succeeds without requiring nullable value types be defined as keys.
    /// Nullable value types are represented in runtime as Nullable<t>. Whereas
    /// nullable reference types do no have a standalone runtime representation.
    /// See csharplang discussion on why typeof(string?) (nullable reference type) is not valid,
    /// and that the type encountered during runtime for string? would be string.
    /// </summary>
    /// <param name="nullableType"></param>
    /// <seealso cref="https://github.com/dotnet/csharplang/discussions/3003"/>
    [DataRow(typeof(int?))]
    [DataRow(typeof(byte?))]
    [DataRow(typeof(sbyte?))]
    [DataRow(typeof(short?))]
    [DataRow(typeof(ushort?))]
    [DataRow(typeof(int?))]
    [DataRow(typeof(uint?))]
    [DataRow(typeof(long?))]
    [DataRow(typeof(ulong?))]
    [DataRow(typeof(float?))]
    [DataRow(typeof(double?))]
    [DataRow(typeof(decimal?))]
    [DataRow(typeof(bool?))]
    [DataRow(typeof(char?))]
    [DataRow(typeof(Guid?))]
    [DataRow(typeof(TimeOnly?))]
    [DataRow(typeof(TimeSpan?))]
    [TestMethod]
    public void ResolveUnderlyingTypeForNullableValueType(Type nullableType)
    {
        Assert.AreNotEqual(notExpected: JsonDataType.Undefined, actual: TypeHelper.GetJsonDataTypeFromSystemType(nullableType));
        Assert.IsNotNull(TypeHelper.GetDbTypeFromSystemType(nullableType));
    }
}
