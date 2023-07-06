// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.OpenAPI;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OpenApiDocumentor
{
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
        public void SupportedSystemTypesMapToJsonValueType()
        {
            foreach (KeyValuePair<string, bool> sqlDataType in SqlTypeConstants.SupportedSqlDbTypes)
            {
                string sqlDataTypeLiteral = sqlDataType.Key;
                bool isSupportedSqlDataType = sqlDataType.Value;

                try
                {
                    Type resolvedType = TypeHelper.GetSystemTypeFromSqlDbType(sqlDataTypeLiteral);
                    Assert.AreEqual(true, isSupportedSqlDataType, ERROR_PREFIX + $" {{{sqlDataTypeLiteral}}} " + SQLDBTYPE_RESOLUTION_ERROR);

                    JsonDataType resolvedJsonType = TypeHelper.GetJsonDataTypeFromSystemType(resolvedType);
                    Assert.AreEqual(isSupportedSqlDataType, resolvedJsonType != JsonDataType.Undefined, ERROR_PREFIX + $" {{{sqlDataTypeLiteral}}} " + JSONDATATYPE_RESOLUTION_ERROR);

                    DbType? resolvedDbType = TypeHelper.GetDbTypeFromSystemType(resolvedType);
                    Assert.AreEqual(isSupportedSqlDataType, resolvedDbType is not null, ERROR_PREFIX + $" {{{sqlDataTypeLiteral}}} " + DBTYPE_RESOLUTION_ERROR);
                }
                catch (DataApiBuilderException)
                {
                    Assert.AreEqual(false, isSupportedSqlDataType, ERROR_PREFIX + $" {{{sqlDataTypeLiteral}}} " + SQLDBTYPE_RESOLUTION_ERROR);
                }
            }
        }
    }
}
