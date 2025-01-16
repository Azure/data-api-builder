// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes
{
    /// <summary>
    /// Only used to group the supported type names under a class with a relevant name.
    /// The type names mentioned here are Hotchocolate scalar built in types.
    /// The corresponding SQL type name may be different for e.g. UUID maps to Guid as the SQL type.
    /// </summary>
    public static class SupportedHotChocolateTypes
    {
        public const string UUID_TYPE = "UUID";
        public const string BYTE_TYPE = "Byte";
        public const string SHORT_TYPE = "Short";
        public const string INT_TYPE = "Int";
        public const string LONG_TYPE = "Long";
        public const string SINGLE_TYPE = "Single";
        public const string FLOAT_TYPE = "Float";
        public const string DECIMAL_TYPE = "Decimal";
        public const string STRING_TYPE = "String";
        public const string BOOLEAN_TYPE = "Boolean";
        public const string BYTEARRAY_TYPE = "ByteArray";
        public const string DATETIME_TYPE = "DateTime";
        public const string DATETIMEOFFSET_TYPE = "DateTimeOffset";
        public const string LOCALTIME_TYPE = "LocalTime";
        public const string TIME_TYPE = "Time";
    }

    /// <summary>
    /// Class representing the sql datetime types supported by DAB which in addition to the sql datetime type,
    /// all map to the same .NET type of DateTime and Hotchocolate scalar type of DateTime.
    /// </summary>
    public static class SupportedDateTimeTypes
    {
        public const string DATE_TYPE = "date";
        public const string SMALLDATETIME_TYPE = "smalldatetime";
        public const string DATETIME2_TYPE = "datetime2";
    }

    /// <summary>
    /// class representing mapping between hotchocolate types and return type for aggregate.
    /// </summary>
    public static class SupportedAggregateTypes
    {
        public static HashSet<string> NumericAggregateTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            SupportedHotChocolateTypes.LONG_TYPE,
            SupportedHotChocolateTypes.INT_TYPE,
            SupportedHotChocolateTypes.SHORT_TYPE,
            SupportedHotChocolateTypes.DECIMAL_TYPE,
            SupportedHotChocolateTypes.FLOAT_TYPE,
            SupportedHotChocolateTypes.BYTE_TYPE,
        };
    }
}
