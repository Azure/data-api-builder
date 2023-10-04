// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models;

/// <summary>
/// Represents a Result Set read from DbDataReader.
/// </summary>
public class DbResultSet
{
    public DbResultSet(
        Dictionary<string, object> resultProperties)
    {
        Rows = new();
        ResultProperties = resultProperties;
    }

    /// <summary>
    /// Represents the rows in the result set.
    /// </summary>
    public List<DbResultSetRow> Rows { get; private set; }

    /// <summary>
    /// Represents DbDataReader properties such as RecordsAffected and HasRows.
    /// </summary>
    public Dictionary<string, object> ResultProperties { get; private set; }
}

/// <summary>
/// Represents a single row present in the Result Set.
/// </summary>
public class DbResultSetRow
{
    public DbResultSetRow() { }

    /// <summary>
    /// Represents a result set row in <c>ColumnName: Value</c> format, empty if no row was found.
    /// </summary>
    public Dictionary<string, object?> Columns { get; private set; } = new();
}
