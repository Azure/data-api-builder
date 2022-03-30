using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;

namespace Azure.DataGateway.Service.Resolvers
{

    /// <summary>
    /// Holds shared properties and methods among
    /// Sql*QueryStructure classes
    /// </summary>
    public abstract class BaseSqlQueryStructure : BaseQueryStructure
    {
        protected SqlGraphQLFileMetadataProvider MetadataStoreProvider { get; }

        /// <summary>
        /// The name of the main table to be queried.
        /// </summary>
        public string TableName { get; protected set; }
        /// <summary>
        /// The alias of the main table to be queried.
        /// </summary>
        public string TableAlias { get; protected set; }

        /// <summary>
        /// FilterPredicates is a string that represents the filter portion of our query
        /// in the WHERE Clause. This is generated specifically from the $filter portion
        /// of the query string.
        /// </summary>
        public string? FilterPredicates { get; set; }

        public BaseSqlQueryStructure(
            SqlGraphQLFileMetadataProvider metadataStoreProvider,
            IncrementingInteger? counter = null,
            string tableName = "")
            : base(counter)
        {
            MetadataStoreProvider = metadataStoreProvider;

            TableName = tableName;
            // Default the alias to the table name
            TableAlias = tableName;
        }

        /// <summary>
        /// Get column type from table underlying the query strucutre
        /// </summary>
        public ColumnType GetColumnType(string columnName)
        {
            if (GetTableDefinition().Columns.TryGetValue(columnName, out ColumnDefinition? column))
            {
                return column.Type;
            }
            else
            {
                throw new ArgumentException($"{columnName} is not a valid column of {TableName}");
            }
        }

        /// <summary>
        /// Returns the TableDefinition for the the table of this query.
        /// </summary>
        protected TableDefinition GetTableDefinition()
        {
            return MetadataStoreProvider.GetTableDefinition(TableName);
        }

        /// <summary>
        /// Get primary key as list of string
        /// </summary>
        public List<string> PrimaryKey()
        {
            return GetTableDefinition().PrimaryKey;
        }

        /// <summary>
        /// get all columns of the table
        /// </summary>
        public List<string> AllColumns()
        {
            return GetTableDefinition().Columns.Select(col => col.Key).ToList();
        }

        ///<summary>
        /// Gets the value of the parameter cast as the type of the column this parameter is associated with
        ///</summary>
        /// <exception cref="ArgumentException">columnName is not a valid column of table or param
        /// does not have a valid value type</exception>
        protected object GetParamAsColumnSystemType(string param, string columnName)
        {
            ColumnType type = GetColumnType(columnName);
            Type systemType = ColumnDefinition.ResolveColumnTypeToSystemType(type);

            try
            {
                switch (systemType.Name)
                {
                    case "String":
                        return param;
                    case "Int64":
                        return long.Parse(param);
                    default:
                        // should never happen due to the config being validated for correct types
                        throw new NotSupportedException($"{type} is not supported");
                }
            }
            catch (Exception e)
            {
                if (e is FormatException ||
                    e is ArgumentNullException ||
                    e is OverflowException)
                {
                    throw new ArgumentException($"Parameter \"{param}\" cannot be resolved as column \"{columnName}\" with type \"{type}\".");
                }

                throw;
            }
        }
    }
}
