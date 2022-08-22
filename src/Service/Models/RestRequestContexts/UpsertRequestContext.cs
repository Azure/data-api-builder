using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Service.Models
{
    /// <summary>
    /// UpsertRequestContext provides the major components of a REST query
    /// corresponding to the PutOne operations.
    /// </summary>
    public class UpsertRequestContext : RestRequestContext
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public UpsertRequestContext(
            string entityName,
            DatabaseObject dbo,
            JsonElement insertPayloadRoot,
            Operation operationType)
            : base(entityName, dbo)
        {
            FieldsToBeReturned = new();
            PrimaryKeyValuePairs = new();
            OperationType = operationType;

            PopulateFieldValuePairsInBody(insertPayloadRoot);

            // We don't support UpsertMany as yet.
            IsMany = false;
        }
    }
}

