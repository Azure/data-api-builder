// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;

namespace Azure.DataApiBuilder.Core.Models;

/// <summary>
/// Represents a single parameter created for the database connection.
/// </summary>
public class DbConnectionParam
{
    public DbConnectionParam(object? value, DbType? dbType = null, SqlDbType? sqlDbType = null, int? length = null)
    {
        Value = value;
        DbType = dbType;
        SqlDbType = sqlDbType;
        Length = length;
    }

    /// <summary>
    /// Value of the parameter.
    /// </summary>
    public object? Value { get; set; }

    // DbType of the parameter.
    // This is being made nullable because GraphQL treats Sql Server types like datetime, datetimeoffset
    // identically and then implicit conversion cannot happen.
    // For more details refer: https://github.com/Azure/data-api-builder/pull/1442.
    public DbType? DbType { get; set; }

    // This is being made nullable
    // because it's not populated for DB's other than MSSQL.
    public SqlDbType? SqlDbType { get; set; }

    // Nullable integer parameter representing length. nullable for back compatibility and for where its not needed
    public int? Length { get; set; }
}
