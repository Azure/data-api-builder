// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes
{
    /// <summary>
    /// Only used to group the supported type names under a class with a relevant name
    /// </summary>
    public static class SupportedTypes
    {
        public const string BYTE_TYPE = "Byte";
        public const string SHORT_TYPE = "Short";
        public const string INT_TYPE = "Int";
        public const string LONG_TYPE = "Long";
        public const string SINGLE_TYPE = "Single";
        public const string FLOAT_TYPE = "Float";
        public const string DECIMAL_TYPE = "Decimal";
        public const string STRING_TYPE = "String";
        public const string BOOLEAN_TYPE = "Boolean";
        public const string DATETIME_TYPE = "DateTime";
        // The DATETIME_NONUTC_TYPE constant is only used in testing
        // since PostgreSQL doesn't support datetime values with a non-UTC time zone.
        public const string DATETIME_NONUTC_TYPE = "DateTimeNonUTC";
        public const string BYTEARRAY_TYPE = "ByteArray";
        public const string TIMESPAN_TYPE = "TimeSpan";
        public const string GUID_TYPE = "Guid";
    }
}
