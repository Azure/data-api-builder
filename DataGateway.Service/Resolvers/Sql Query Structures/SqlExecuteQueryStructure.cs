using System.Collections.Generic;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Services;

namespace Azure.DataGateway.Service.Resolvers
{
    public class SqlExecuteStructure : BaseSqlQueryStructure
    {
        // Holds the final, resolved parameters that will be passed when building the execute stored procedure query
        public Dictionary<string, object> ProcedureParameters { get; set; }
        /// <summary>
        /// requestParams will be resolved from either the request querystring or body by this point
        /// construct the ProcedureParameters dictionary through resolving requestParams and defaults from config/metadata
        /// </summary>
        public SqlExecuteStructure(
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            IDictionary<string, object?> requestParams)
        : base(sqlMetadataProvider, entityName: entityName)
        {
            StoredProcedureDefinition storedProcedureDefinition = SqlMetadataProvider.GetStoredProcedureDefinition(entityName);
            ProcedureParameters = new();
            foreach((string paramKey, ParameterDefinition paramValue) in storedProcedureDefinition.Parameters)
            {
                // populate with request param if able
                if (requestParams.TryGetValue(paramKey, out object? requestParamValue))
                {
                    ProcedureParameters.Add(paramKey, requestParamValue!);
                }
                else
                { // fill with default value
                    ProcedureParameters.Add(paramKey, paramValue.ConfigDefaultValue!);
                } 
            }
        }
    }
}
