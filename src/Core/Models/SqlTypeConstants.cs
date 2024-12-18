// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Service.Models;

/// <summary>
/// String literal representation of SQL Server data types that are returned by Microsoft.Data.SqlClient.
/// </summary>
/// <seealso cref="https://github.com/dotnet/SqlClient/blob/main/src/Microsoft.Data.SqlClient/tests/PerformanceTests/Config/Constants.cs"/>
public static class SqlTypeConstants
{
    /// <summary>
    /// SqlDbType names ordered by corresponding SqlDbType.
    /// Keys are lower case to match formatting of SQL Server INFORMATION_SCHEMA DATA_TYPE column value.
    /// Sourced directly from internal/private SmiMetaData.cs in Microsoft.Data.SqlClient
    /// Value indicates whether DAB engine supports the SqlDbType.
    /// </summary>
    /// <seealso cref="https://github.com/dotnet/SqlClient/blob/2b31810ce69b88d707450e2059ee8fbde63f774f/src/Microsoft.Data.SqlClient/src/Microsoft/Data/SqlClient/Server/SmiMetaData.cs#L637-L674"/>
    public static readonly Dictionary<string, bool> SupportedSqlDbTypes = new()
    {
        { "bigint", true },               // SqlDbType.BigInt
        { "binary", true },               // SqlDbType.Binary
        { "bit", true },                  // SqlDbType.Bit
        { "char", true },                 // SqlDbType.Char
        { "datetime", true },             // SqlDbType.DateTime
        { "decimal", true },              // SqlDbType.Decimal
        { "float", true },                // SqlDbType.Float
        { "image", true },                // SqlDbType.Image
        { "int", true },                  // SqlDbType.Int
        { "money", true },                // SqlDbType.Money
        { "nchar", true },                // SqlDbType.NChar
        { "ntext", true },                // SqlDbType.NText
        { "nvarchar", true },             // SqlDbType.NVarChar
        { "real", true },                 // SqlDbType.Real
        { "uniqueidentifier", true },     // SqlDbType.UniqueIdentifier
        { "smalldatetime", true },        // SqlDbType.SmallDateTime
        { "smallint", true },             // SqlDbType.SmallInt
        { "smallmoney", true },           // SqlDbType.SmallMoney
        { "text", true },                 // SqlDbType.Text
        { "timestamp", true },            // SqlDbType.Timestamp
        { "tinyint", true },              // SqlDbType.TinyInt
        { "varbinary", true },            // SqlDbType.VarBinary
        { "varchar", true },              // SqlDbType.VarChar
        { "sql_variant", false },         // SqlDbType.Variant (unsupported)
        { "xml", false },                 // SqlDbType.Xml (unsupported)
        { "date", true },                 // SqlDbType.Date
        { "time", true },                 // SqlDbType.Time
        { "datetime2", true },            // SqlDbType.DateTime2
        { "datetimeoffset", true },       // SqlDbType.DateTimeOffset
        { "", false },                    // SqlDbType.Udt and SqlDbType.Structured provided by SQL as empty strings (unsupported)
        { "numeric", true}                // Not present in SqlDbType, however can be returned by sql functions like LAG and should map to decimal.
    };
}
