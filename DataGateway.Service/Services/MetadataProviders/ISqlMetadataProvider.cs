using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Azure.DataGateway.Config;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// Interface to retrieve information for the runtime from the database.
    /// </summary>
    public interface ISqlMetadataProvider
    {
        /// <summary>
        /// Gets the DataTable from the EntitiesDataSet if already present.
        /// If not present, fills it first and returns the same.
        /// </summary>
        public Task<DataTable> GetTableWithSchemaFromDataSetAsync(
            string schemaName,
            string tableName);

        /// <summary>
        /// Fills the table definition with information of all columns and
        /// primary keys.
        /// </summary>
        /// <param name="schemaName">Name of the schema.</param>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="tableDefinition">Table definition to fill.</param>
        public Task PopulateTableDefinitionAsync(
            string schemaName,
            string tableName,
            TableDefinition tableDefinition);

        /// <summary>
        /// Fills the table definition with information of the foreign keys
        /// for all the tables.
        /// </summary>
        /// <param name="schemaName">Name of the default schema.</param>
        /// <param name="tables">Dictionary of all tables.</param>
        public Task PopulateForeignKeyDefinitionAsync(
            string schemaName,
            IEnumerable<SqlEntity> sqlEntities);
    }
}
