using System.Text.Json;
using Azure.DataGateway.Config;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// StoredProcedureRequestContext provides all needed request context for a stored procedure query.
    /// For Find requests, parameters will be passed in the query string, which we can access from the base class's
    /// ParsedQueryString field; for all other Operation types, we can populate and use the FieldValuePairsInBody
    /// </summary>
    public class StoredProcedureRequestContext : RestRequestContext
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public StoredProcedureRequestContext(
            string entityName,
            DatabaseObject dbo,
            JsonElement? requestPayloadRoot,
            Operation operationType)
            : base(entityName, dbo)
        {
            FieldsToBeReturned = new();
            OperationType = operationType;

            PopulateFieldValuePairsInBody(requestPayloadRoot);

        }

    }
}
