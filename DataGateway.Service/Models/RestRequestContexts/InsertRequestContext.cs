using System.Text.Json;
using Azure.DataGateway.Config;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// InsertRequestContext provides the major components of a REST query
    /// corresponding to the InsertOne or InsertMany operations.
    /// </summary>
    public class InsertRequestContext : RestRequestContext
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public InsertRequestContext(
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
            // We don't support InsertMany as yet.
            IsMany = false;
        }
    }
}
