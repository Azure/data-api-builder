using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.AspNetCore.Mvc;

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
        /// Represents the parameters that this request is calling the stored procedure with
        /// </summary>
        public Dictionary<string, object?>? ResolvedParameters { get; set; }

        /// <summary>
        /// Represents a request to execute a stored procedure. At the time of construction, populates the FieldValuePairsInBody
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

        /// <summary>
        /// Resolves the parameters that will be passed to the SqlExecuteQueryStructure constructor
        /// This method should be called after the FieldValuePairsInBody and ParsedQueryString collections are filled
        /// For Find operation, parameters are resolved using the query string; for all others, the request body
        /// </summary>
        public void PopulateResolvedParameters()
        {
            if (OperationType is Operation.Find)
            {
                if (ParsedQueryString is not null)
                {
                    // Query string may have malformed/null keys, if so just ignore them
                    ResolvedParameters = ParsedQueryString.Cast<string>()
                        .Where(k => k != null).ToDictionary(k => k, k => (object?)ParsedQueryString[k]);
                }
                else
                {
                    ResolvedParameters = new();
                }
            }
            else
            {
                ResolvedParameters = FieldValuePairsInBody;
            }
        }

        /// <summary>
        /// Implements the visitor pattern/double dispatch
        /// Helps avoid dynamic cast or downcast in SqlQueryEngine
        /// </summary>
        public override Task<IActionResult> DispatchExecute(IQueryEngine _queryEngine)
        {
            return _queryEngine.ExecuteAsync(this);
        }

    }
}
