// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;

namespace Azure.DataApiBuilder.Service.Models
{
    /// <summary>
    /// Represents a single parameter created for the database connection.
    /// </summary>
    public class DbConnectionParam
    {
        public DbConnectionParam(object? value, DbType? dbType = null)
        {
            Value = value;
            DbType = dbType;
        }

        /// <summary>
        /// Value of the parameter.
        /// </summary>
        public object? Value { get; set; }

        // DbType of the parameter.
        public DbType? DbType { get; set; }
    }
}
