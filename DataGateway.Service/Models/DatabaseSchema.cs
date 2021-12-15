using System.Collections.Generic;
using Azure.DataGateway.Service.Authorization;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// The schema of the database described in a JSON format.
    /// </summary>
    public class DatabaseSchema
    {
        public Dictionary<string, TableDefinition> Tables { get; set; } = new();
    }

    public class TableDefinition
    {
        /// <summary>
        /// The list of columns that together form the primary key of the table.
        /// </summary>
        public List<string> PrimaryKey { get; set; } = new();
        public Dictionary<string, ColumnDefinition> Columns { get; set; } = new();
        public Dictionary<string, ForeignKeyDefinition> ForeignKeys { get; set; } = new();
        public Dictionary<string, AuthorizationRule> Operations { get; set; } = new();
    }

    public class ColumnDefinition
    {
        /// <summary>
        /// The database type of this column
        /// </summary>
        public string Type { get; set; }
    }

    public class ForeignKeyDefinition
    {
        public string ReferencedTable { get; set; }
        /// <summary>
        /// The list of columns that together reference the primary key of the
        /// referenced table. The order of these columns should corespond to
        /// the order of the columns of the primary key.
        /// </summary>
        public List<string> Columns { get; set; } = new();
    }

    public class AuthorizationRule
    {
        public AuthorizationType AuthorizationType { get; set; }
    }
}
