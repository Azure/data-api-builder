using System.Data;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;

/// <summary>
/// Interface to retrieve information for the runtime from the database.
/// </summary>
public interface ISqlMetadataProvider
{
    /// <summary>
    /// Gets the DataTable from the EntitiesDataSet if already present.
    /// If not present, fills it first and returns the same.
    /// </summary>
    public Task<DataTable> GetTableWithSchemaFromDataSet(
        string schemaName,
        string tableName);

    /// <summary>
    /// Fills the table definition with information of all columns and
    /// primary keys.
    /// </summary>
    /// <param name="schemaName">Name of the schema.</param>
    /// <param name="tableName">Name of the table.</param>
    /// <param name="tableDefinition">Table definition to fill.</param>
    public Task PopulateTableDefinition(
        string schemaName,
        string tableName,
        TableDefinition tableDefinition);
}
