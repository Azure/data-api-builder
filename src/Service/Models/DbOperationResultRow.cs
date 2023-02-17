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
            Dictionary<string, object?> columns,
            Dictionary<string, object> resultProperties)
        {
            this.Columns = columns;
            this.ResultProperties = resultProperties;
        }

        /// <summary>
        /// Represents a result set row in <c>ColumnName: Value</c> format, empty if no row was found.
        /// </summary>
        public Dictionary<string, object?> Columns { get; private set; }

        /// <summary>
        /// Represents DbDataReader properties such as RecordsAffected and HasRows.
        /// </summary>
        public Dictionary<string, object> ResultProperties { get; private set; }
    }
}
