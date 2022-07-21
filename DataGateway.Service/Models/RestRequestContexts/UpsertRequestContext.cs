using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;

namespace Azure.DataGateway.Service.Models
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

