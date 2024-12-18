// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using Azure.DataApiBuilder.Core.Services.OpenAPI;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Language;
using Microsoft.OData.Edm;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Type mapping helpers to convert between SQL Server types, .NET Framework types, and Json value types.
    /// </summary>
    /// <seealso cref="https://learn.microsoft.com/dotnet/framework/data/adonet/sql-server-data-type-mappings"/>
    public static class TypeHelper
    {
        /// <summary>
        /// Maps .NET Framework types to DbType enum
        /// Not Adding a hard mapping for System.DateTime to DbType.DateTime as
        /// Hotchocolate only has Hotchocolate.Types.DateTime for DbType.DateTime/DateTime2/DateTimeOffset,
        /// which throws error when inserting/updating dateTime values due to type mismatch.
        /// Therefore, seperate logic exists for proper mapping conversion in BaseSqlQueryStructure.
        /// </summary>
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
            [typeof(DateTimeOffset)] = DbType.DateTimeOffset,
            [typeof(byte[])] = DbType.Binary,
            [typeof(TimeOnly)] = DbType.Time,
            [typeof(TimeSpan)] = DbType.Time,
            [typeof(object)] = DbType.Object
        };

        /// <summary>
        /// Maps .NET Framework type (System/CLR type) to JsonDataType.
        /// Unnecessary to add nullable types because GetJsonDataTypeFromSystemType()
        /// (the helper used to access key/values in this dictionary)
        /// resolves the underlying type when a nullable type is used for lookup.
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
            [typeof(TimeSpan)] = JsonDataType.String,
            [typeof(TimeOnly)] = JsonDataType.String,
            [typeof(object)] = JsonDataType.Object,
            [typeof(DateTime)] = JsonDataType.String,
            [typeof(DateTimeOffset)] = JsonDataType.String
        };

        /// <summary>
        /// Maps SqlDbType enum to .NET Framework type (System type).
        /// </summary>
        private static readonly Dictionary<SqlDbType, Type> _sqlDbTypeToType = new()
        {
            [SqlDbType.BigInt] = typeof(long),
            [SqlDbType.Binary] = typeof(byte),
            [SqlDbType.Bit] = typeof(bool),
            [SqlDbType.Char] = typeof(string),
            [SqlDbType.Date] = typeof(DateTime),
            [SqlDbType.DateTime] = typeof(DateTime),
            [SqlDbType.DateTime2] = typeof(DateTime),
            [SqlDbType.DateTimeOffset] = typeof(DateTimeOffset),
            [SqlDbType.Decimal] = typeof(decimal),
            [SqlDbType.Float] = typeof(double),
            [SqlDbType.Image] = typeof(byte[]),
            [SqlDbType.Int] = typeof(int),
            [SqlDbType.Money] = typeof(decimal),
            [SqlDbType.NChar] = typeof(char),
            [SqlDbType.NText] = typeof(string),
            [SqlDbType.NVarChar] = typeof(string),
            [SqlDbType.Real] = typeof(float),
            [SqlDbType.SmallDateTime] = typeof(DateTime),
            [SqlDbType.SmallInt] = typeof(short),
            [SqlDbType.SmallMoney] = typeof(decimal),
            [SqlDbType.Text] = typeof(string),
            [SqlDbType.Time] = typeof(TimeOnly),
            [SqlDbType.Timestamp] = typeof(byte[]),
            [SqlDbType.TinyInt] = typeof(byte),
            [SqlDbType.UniqueIdentifier] = typeof(Guid),
            [SqlDbType.VarBinary] = typeof(byte[]),
            [SqlDbType.VarChar] = typeof(string)
        };

        private static Dictionary<SqlDbType, DbType> _sqlDbDateTimeTypeToDbType = new()
        {
            [SqlDbType.Date] = DbType.Date,
            [SqlDbType.DateTime] = DbType.DateTime,
            [SqlDbType.SmallDateTime] = DbType.DateTime,
            [SqlDbType.DateTime2] = DbType.DateTime2,
            [SqlDbType.DateTimeOffset] = DbType.DateTimeOffset
        };

        /// <summary>
        /// Given the system type, returns the corresponding primitive type kind.
        /// </summary>
        /// <param name="columnSystemType">Type of the column.</param>
        /// <returns>EdmPrimitiveTypeKind</returns>
        /// <exception cref="ArgumentException">Throws when the column</exception>
        public static EdmPrimitiveTypeKind GetEdmPrimitiveTypeFromSystemType(Type columnSystemType)
        {
            if (columnSystemType.IsArray)
            {
                columnSystemType = columnSystemType.GetElementType()!;
            }

            EdmPrimitiveTypeKind type = columnSystemType.Name switch
            {
                "String" => EdmPrimitiveTypeKind.String,
                "Guid" => EdmPrimitiveTypeKind.Guid,
                "Byte" => EdmPrimitiveTypeKind.Byte,
                "Int16" => EdmPrimitiveTypeKind.Int16,
                "Int32" => EdmPrimitiveTypeKind.Int32,
                "Int64" => EdmPrimitiveTypeKind.Int64,
                "Single" => EdmPrimitiveTypeKind.Single,
                "Double" => EdmPrimitiveTypeKind.Double,
                "Decimal" => EdmPrimitiveTypeKind.Decimal,
                "Boolean" => EdmPrimitiveTypeKind.Boolean,
                "DateTime" => EdmPrimitiveTypeKind.DateTimeOffset,
                "DateTimeOffset" => EdmPrimitiveTypeKind.DateTimeOffset,
                "Date" => EdmPrimitiveTypeKind.Date,
                "TimeOnly" => EdmPrimitiveTypeKind.TimeOfDay,
                "TimeSpan" => EdmPrimitiveTypeKind.TimeOfDay,
                _ => throw new ArgumentException($"Column type" +
                        $" {columnSystemType.Name} not yet supported.")
            };

            return type;
        }

        /// <summary>
        /// Given GraphQl type, returns the corresponding primitive type kind.
        /// </summary>
        /// <param name="columnSystemType">Type of the column.</param>
        /// <returns>EdmPrimitiveTypeKind</returns>
        /// <exception cref="ArgumentException">Throws when the column</exception>
        public static EdmPrimitiveTypeKind GetEdmPrimitiveTypeFromITypeNode(ITypeNode columnSystemType)
        {
            string graphQlType;
            if (columnSystemType.IsListType())
            {
                graphQlType = ((ListTypeNode)columnSystemType).NamedType().Name.Value;
            }
            else if (columnSystemType.IsNonNullType())
            {
                graphQlType = ((NonNullTypeNode)columnSystemType).NamedType().Name.Value;
            }
            else
            {
                graphQlType = ((NamedTypeNode)columnSystemType).Name.Value;
            }

            // https://graphql.org/learn/schema/#scalar-types
            EdmPrimitiveTypeKind type = graphQlType switch
            {
                "String" => EdmPrimitiveTypeKind.String,
                "ID" => EdmPrimitiveTypeKind.Guid,
                "Int" => EdmPrimitiveTypeKind.Int32,
                "Float" => EdmPrimitiveTypeKind.Decimal,
                "Boolean" => EdmPrimitiveTypeKind.Boolean,
                "Date" => EdmPrimitiveTypeKind.Date,
                _ => EdmPrimitiveTypeKind.PrimitiveType
            };

            return type;
        }

        /// <summary>
        /// Converts the .NET Framework (System/CLR) type to JsonDataType.
        /// Primitive data types in the OpenAPI standard (OAS) are based on the types supported
        /// by the JSON Schema Specification Wright Draft 00.
        /// The value returned is formatted for use in the OpenAPI spec "type" property.
        /// </summary>
        /// <param name="type">CLR type</param>
        /// <seealso cref="https://spec.openapis.org/oas/v3.0.1#data-types"/>
        /// <returns>Formatted JSON type name in lower case: e.g. number, string, boolean, etc.</returns>
        public static JsonDataType GetJsonDataTypeFromSystemType(Type type)
        {
            // Get the underlying type argument if the 'type' argument is a nullable type.
            Type? nullableUnderlyingType = Nullable.GetUnderlyingType(type);

            // Will not be null when the input argument 'type' is a closed generic nullable type.
            if (nullableUnderlyingType is not null)
            {
                type = nullableUnderlyingType;
            }

            if (!_systemTypeToJsonDataTypeMap.TryGetValue(type, out JsonDataType openApiJsonTypeName))
            {
                openApiJsonTypeName = JsonDataType.Undefined;
            }

            return openApiJsonTypeName;
        }

        /// <summary>
        /// Returns the DbType for given system type.
        /// </summary>
        /// <param name="systemType">The system type for which the DbType is to be determined.</param>
        /// <returns>DbType for the given system type. Null when no mapping exists.</returns>
        public static DbType? GetDbTypeFromSystemType(Type systemType)
        {
            // Get the underlying type argument if the 'systemType' argument is a nullable type.
            Type? nullableUnderlyingType = Nullable.GetUnderlyingType(systemType);

            // Will not be null when the input argument 'systemType' is a closed generic nullable type.
            if (nullableUnderlyingType is not null)
            {
                systemType = nullableUnderlyingType;
            }

            if (!_systemTypeToDbTypeMap.TryGetValue(systemType, out DbType dbType))
            {
                return null;
            }

            return dbType;
        }

        /// <summary>
        /// Converts the string representation of a SQL Server data type that can be parsed into SqlDbType enum
        /// to the corrsponding .NET Framework/CLR type as documented by the SQL Server data type mappings article.
        /// The SQL Server database engine type and SqlDbType enum map 1:1 when character casing is ignored.
        /// e.g. SQL DB type 'bigint' maps to SqlDbType enum 'BigInt' in a case-insensitive match.
        /// There are some mappings in the SQL Server data type mappings table which do not map after ignoring casing, however
        /// those mappings are outdated and don't accommodate newly added SqlDbType enum values.
        /// e.g. The documentation table shows SQL server type 'binary' maps to SqlDbType enum 'VarBinary',
        /// however SqlDbType.Binary now exists.
        /// </summary>
        /// <param name="dbTypeName">String value sourced from the DATA_TYPE column in the Procedure Parameters or Columns
        /// schema collections.</param>
        /// <seealso cref="https://learn.microsoft.com/dotnet/framework/data/adonet/sql-server-schema-collections#columns"/>
        /// <seealso cref="https://learn.microsoft.com/dotnet/framework/data/adonet/sql-server-schema-collections#procedure-parameters"/>
        /// <exception>Failed type conversion.</exception>"
        public static Type GetSystemTypeFromSqlDbType(string sqlDbTypeName)
        {
            // Remove the length specifier from the type name if it exists.Example: varchar(50) -> varchar
            int separatorIndex = sqlDbTypeName.IndexOf('(');
            string baseType = separatorIndex == -1 ? sqlDbTypeName : sqlDbTypeName.Substring(0, separatorIndex);

            if (Enum.TryParse(baseType, ignoreCase: true, out SqlDbType sqlDbType))
            {
                if (_sqlDbTypeToType.TryGetValue(sqlDbType, out Type? value))
                {
                    return value;
                }
            }
            else if (baseType.Equals("numeric", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(decimal);
            }

            throw new DataApiBuilderException(
                message: $"Tried to convert unsupported data type: {sqlDbTypeName}",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
        }

        /// <summary>
        /// Helper method to get the DbType corresponding to the given SqlDb datetime type.
        /// </summary>
        /// <param name="sqlDbType">Underlying sqlDbType of the parameter.</param>
        /// <param name="dbType">DbType of the parameter corresponding to its sqlDbType.</param>
        /// <returns>True if given DateTime sqlDbType is supported by DAB, else false.</returns>
        public static bool TryGetDbTypeFromSqlDbDateTimeType(SqlDbType sqlDbType, [NotNullWhen(true)] out DbType dbType)
        {
            return _sqlDbDateTimeTypeToDbType.TryGetValue(sqlDbType, out dbType);
        }

        /// <summary>
        /// This function identifies the value type and converts that data in return.
        /// </summary>
        /// <param name="node"></param>
        /// <returns>Identify the value type and convert that data in return</returns>
        public static object? GetValue(IValueNode node)
        {
            SyntaxKind valueKind = node.Kind;
            return valueKind switch
            {
                SyntaxKind.IntValue => Convert.ToInt32(node.Value, CultureInfo.InvariantCulture), // spec
                SyntaxKind.FloatValue => Convert.ToDouble(node.Value, CultureInfo.InvariantCulture), // spec
                SyntaxKind.BooleanValue => Convert.ToBoolean(node.Value), // spec
                SyntaxKind.StringValue => Convert.ToString(node.Value, CultureInfo.InvariantCulture), // spec
                SyntaxKind.NullValue => null, // spec
                _ => Convert.ToString(node.Value, CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// This function identifies if the value type is primitive or not.
        /// </summary>
        public static bool IsPrimitiveType(SyntaxKind kind)
        {
            return (kind is SyntaxKind.IntValue) ||
                (kind is SyntaxKind.FloatValue) ||
                (kind is SyntaxKind.BooleanValue) ||
                (kind is SyntaxKind.StringValue) ||
                (kind is SyntaxKind.NullValue);
        }
    }
}
