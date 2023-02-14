// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Azure.DataApiBuilder.Service.Models
{
    /// <summary>
    /// Represents a single row read from DbDataReader.
    /// </summary>
    public class DbOperationResultRow
    {
        public DbOperationResultRow(
            Dictionary<string, object?> row,
            Dictionary<string, object> propertiesOfResult)
        {
            this.Row = row;
            this.PropertiesOfResult = propertiesOfResult;
        }

        /// <summary>
        /// A dictionary representing the row in <c>ColumnName: Value</c> format, empty if no row was found.
        /// </summary>
        public Dictionary<string, object?> Row { get; set; }

        /// <summary>
        /// A dictionary of properties of the DbDataReader like RecordsAffected, HasRows.
        /// </summary>
        public Dictionary<string, object> PropertiesOfResult { get; set; }
    }
}
