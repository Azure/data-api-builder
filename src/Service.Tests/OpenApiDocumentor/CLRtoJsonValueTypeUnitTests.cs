// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.OpenAPI;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OpenApiDocumentor;

/// <summary>
/// Validates TypeHelper converters return expected results.
/// </summary>
[TestClass]
public class CLRtoJsonValueTypeUnitTests
{
    private const string ERROR_PREFIX = "The SqlDbType ";
    private const string SQLDBTYPE_RESOLUTION_ERROR = "failed to resolve to SqlDbType.";
    private const string JSONDATATYPE_RESOLUTION_ERROR = "(when supported) should map to a system type and associated JsonDataType.";
    private const string DBTYPE_RESOLUTION_ERROR = "(when supported) should map to a system type and associated DbType.";

    /// <summary>
    /// Validates that all DAB supported CLR types (system types) map to a defined JSON value type.
    /// A DAB supported CLR type is a CLR type mapped from a database value type.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(GetTestData), DynamicDataSourceType.Method)]
    public void SupportedSystemTypesMapToJsonValueType(string sqlDataTypeLiteral, bool isSupportedSqlDataType)
    {
        try
        {
            Type resolvedType = TypeHelper.GetSystemTypeFromSqlDbType(sqlDataTypeLiteral);
            Assert.AreEqual(true, isSupportedSqlDataType, ERROR_PREFIX + $"{{{sqlDataTypeLiteral}}} " + SQLDBTYPE_RESOLUTION_ERROR);

            JsonDataType resolvedJsonType = TypeHelper.GetJsonDataTypeFromSystemType(resolvedType);
            Assert.AreEqual(isSupportedSqlDataType, resolvedJsonType != JsonDataType.Undefined, ERROR_PREFIX + $"{{{sqlDataTypeLiteral}}} " + JSONDATATYPE_RESOLUTION_ERROR);
        }
        catch (DataApiBuilderException)
        {
            Assert.AreEqual(false, isSupportedSqlDataType, ERROR_PREFIX + $"{{{sqlDataTypeLiteral}}} " + SQLDBTYPE_RESOLUTION_ERROR);
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
    private static IEnumerable<object[]> GetTestData()
    {
        List<object[]> testCases = new();

        foreach (KeyValuePair<string, bool> pair in SqlTypeConstants.SupportedSqlDbTypes)
        {
            testCases.Add(new object[] { pair.Key, pair.Value });
        }

        return testCases;
    }
}
