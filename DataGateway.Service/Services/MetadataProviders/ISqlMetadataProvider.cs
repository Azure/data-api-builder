using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;

/// <summary>
/// Interface to retrieve information for the runtime from the database.
/// </summary>
public interface ISqlMetadataProvider
{
    /// <summary>
    /// Refreshes the database schema with table information for the given schema.
    /// This is best effort - some table information may not be accessible so
    /// will not be retrieved.
    /// </summary>
    Task<DatabaseSchema> RefreshDatabaseSchemaWithTablesAsync(string schemaName);

    /// <summary>
    /// Gets the database schema information for the given table.
    /// </summary>
    public TableDefinition GetTableDefinition(string name);
}
