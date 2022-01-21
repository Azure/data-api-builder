using System;
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
        public Dictionary<string, AuthorizationRule> HttpVerbs { get; set; } = new();
    }

    public class ColumnDefinition
    {
        /// <summary>
        /// The database type of this column
        /// </summary>
        public ColumnType Type { get; set; }

        /// <summary>
        /// Resolves the column type to a System.Type
        /// </summary>
        /// <exception cref="ArgumentException"/>
        public static Type ResolveColumnToSystemType(ColumnType type)
        {
            switch (type)
            {
                case ColumnType.Text:
                case ColumnType.Varchar:
                    return typeof(String);
                case ColumnType.Bigint:
                case ColumnType.Int:
                case ColumnType.Smallint:
                    return typeof(Int64);
                default:
                    throw new ArgumentException($"No resolver for colum type {type}");
            }
        }
    }

    public enum ColumnType
    {
        Text, Varchar,
        Bigint, Int, Smallint
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
        /// <summary>
        /// The various type of AuthZ scenarios supported: Anonymous, Authenticated.
        /// </summary>
        public AuthorizationType AuthorizationType { get; set; }
    }
}
