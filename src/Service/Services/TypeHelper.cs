// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using Azure.DataApiBuilder.Service.Services.OpenAPI;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// Helper class used to resolve CLR Type to the associated DbType or JsonDataType
    /// </summary>
    public static class TypeHelper
    {
        private static Dictionary<Type, DbType> _systemTypeToDbTypeMap = new()
        {
            [typeof(byte)] = DbType.Byte,
            [typeof(sbyte)] = DbType.SByte,
            [typeof(short)] = DbType.Int16,
            [typeof(ushort)] = DbType.UInt16,
            [typeof(int)] = DbType.Int32,
            [typeof(uint)] = DbType.UInt32,
            [typeof(long)] = DbType.Int64,
            [typeof(ulong)] = DbType.UInt64,
            [typeof(float)] = DbType.Single,
            [typeof(double)] = DbType.Double,
            [typeof(decimal)] = DbType.Decimal,
            [typeof(bool)] = DbType.Boolean,
            [typeof(string)] = DbType.String,
            [typeof(char)] = DbType.StringFixedLength,
            [typeof(Guid)] = DbType.Guid,
            [typeof(byte[])] = DbType.Binary,
            [typeof(byte?)] = DbType.Byte,
            [typeof(sbyte?)] = DbType.SByte,
            [typeof(short?)] = DbType.Int16,
            [typeof(ushort?)] = DbType.UInt16,
            [typeof(int?)] = DbType.Int32,
            [typeof(uint?)] = DbType.UInt32,
            [typeof(long?)] = DbType.Int64,
            [typeof(ulong?)] = DbType.UInt64,
            [typeof(float?)] = DbType.Single,
            [typeof(double?)] = DbType.Double,
            [typeof(decimal?)] = DbType.Decimal,
            [typeof(bool?)] = DbType.Boolean,
            [typeof(char?)] = DbType.StringFixedLength,
            [typeof(Guid?)] = DbType.Guid,
            [typeof(object)] = DbType.Object
        };

        /// <summary>
        /// Returns the DbType for given system type.
        /// </summary>
        /// <param name="systemType">The system type for which the DbType is to be determined.</param>
        /// <returns>DbType for the given system type.</returns>
        public static DbType? GetDbTypeFromSystemType(Type systemType)
        {
            if (!_systemTypeToDbTypeMap.TryGetValue(systemType, out DbType dbType))
            {
                return null;
            }

            return dbType;
        }

        /// <summary>
        /// Enables lookup of JsonDataType given a CLR Type.
        /// </summary>
        private static Dictionary<Type, JsonDataType> _systemTypeToJsonDataTypeMap = new()
        {
            [typeof(byte)] = JsonDataType.String,
            [typeof(sbyte)] = JsonDataType.String,
            [typeof(short)] = JsonDataType.Number,
            [typeof(ushort)] = JsonDataType.Number,
            [typeof(int)] = JsonDataType.Number,
            [typeof(uint)] = JsonDataType.Number,
            [typeof(long)] = JsonDataType.Number,
            [typeof(ulong)] = JsonDataType.Number,
            [typeof(float)] = JsonDataType.Number,
            [typeof(double)] = JsonDataType.Number,
            [typeof(decimal)] = JsonDataType.Number,
            [typeof(bool)] = JsonDataType.Boolean,
            [typeof(string)] = JsonDataType.String,
            [typeof(char)] = JsonDataType.String,
            [typeof(Guid)] = JsonDataType.String,
            [typeof(byte[])] = JsonDataType.String,
            [typeof(byte?)] = JsonDataType.String,
            [typeof(sbyte?)] = JsonDataType.String,
            [typeof(short?)] = JsonDataType.Number,
            [typeof(ushort?)] = JsonDataType.Number,
            [typeof(int?)] = JsonDataType.Number,
            [typeof(uint?)] = JsonDataType.Number,
            [typeof(long?)] = JsonDataType.Number,
            [typeof(ulong?)] = JsonDataType.Number,
            [typeof(float?)] = JsonDataType.Number,
            [typeof(double?)] = JsonDataType.Number,
            [typeof(decimal?)] = JsonDataType.Number,
            [typeof(bool?)] = JsonDataType.Boolean,
            [typeof(char?)] = JsonDataType.String,
            [typeof(Guid?)] = JsonDataType.String,
            [typeof(object)] = JsonDataType.Object,
            [typeof(DateTime)] = JsonDataType.String,
            [typeof(DateTimeOffset)] = JsonDataType.String
        };

        /// <summary>
        /// Converts the CLR type to JsonDataType
        /// to meet the data type requirement set by the OpenAPI specification.
        /// The value returned is formatted for the OpenAPI spec "type" property.
        /// </summary>
        /// <param name="type">CLR type</param>
        /// <seealso cref="https://spec.openapis.org/oas/v3.0.1#data-types"/>
        /// <returns>Formatted JSON type name in lower case: e.g. number, string, boolean, etc.</returns>
        public static string SystemTypeToJsonDataType(Type type)
        {
            if (!_systemTypeToJsonDataTypeMap.TryGetValue(type, out JsonDataType openApiJsonTypeName))
            {
                openApiJsonTypeName = JsonDataType.Undefined;
            }
            else
            {
                Console.Out.WriteLine("unknown type");
            }

            string formattedOpenApiTypeName = openApiJsonTypeName.ToString().ToLower();
            return formattedOpenApiTypeName;
        }
    }
}
