using System;
using System.Collections.Generic;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Services;

namespace Azure.DataGateway.Service.Resolvers
{
    public class SqlExecuteStructure : BaseSqlQueryStructure
    {
        // Holds the final, resolved parameters that will be passed when building the execute stored procedure query
        // Keys are the stored procedure arguments (e.g. @id, @username...)
        // Values are the engine-generated referencing parameters (e.g. @param0, @param1...)
        public Dictionary<string, object> ProcedureParameters { get; set; }

        /// <summary>
        /// Constructs a structure with all needed components to build an EXECUTE stored procedure call
        /// requestParams will be resolved from either the request querystring or body by this point
        /// Construct the ProcedureParameters dictionary through resolving requestParams and defaults from config/metadata
        /// Also performs type checking at this stage instead of in RequestValidator to prevent code duplication 
        /// </summary>
        public SqlExecuteStructure(
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            IDictionary<string, object?> requestParams)
        : base(sqlMetadataProvider, entityName: entityName)
        {
            StoredProcedureDefinition storedProcedureDefinition = GetUnderlyingStoredProcedureDefinition();
            ProcedureParameters = new();
            foreach ((string paramKey, ParameterDefinition paramDefinition) in storedProcedureDefinition.Parameters)
            {
                // Populate with request param if able
                if (requestParams.TryGetValue(paramKey, out object? requestParamValue))
                {
                    // Parameterize, then add referencing parameter to ProcedureParameters dictionary
                    try
                    {
                        string parameterizedName = MakeParamWithValue(requestParamValue is null ? null :
                            GetParamAsProcedureParameterType(requestParamValue!.ToString()!, paramKey));
                        ProcedureParameters.Add(paramKey, $"@{parameterizedName}");
                    }
                    catch (ArgumentException ex)
                    {
                        // In the case GetParamAsProcedureParameterType fails to parse as SystemType from database metadata
                        throw new DataGatewayException(
                            message: ex.Message,
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                    }
                }
                else
                { // Fill with default value
                    if (paramDefinition.HasConfigDefault)
                    {
                        string parameterizedName = MakeParamWithValue(paramDefinition.ConfigDefaultValue);
                        ProcedureParameters.Add(paramKey, $"@{parameterizedName}");
                    }
                    else
                    {
                        // This case of not all parameters being explicitly between request and config should already be
                        // handled in the request validation stage.
                        throw new DataGatewayException(message: $"Did not provide all procedure params, missing: \"{paramKey}\"",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                    }
                }

            }
        }

        /// <summary>
        /// Gets the value of the parameter cast as the system type
        /// of the stored procedure parameter this parameter is associated with
        /// </summary>
        protected object GetParamAsProcedureParameterType(string param, string procParamName)
        {
            Type systemType = GetUnderlyingStoredProcedureDefinition().Parameters[procParamName].SystemType!;
            try
            {
                return ParseParamAsSystemType(param, systemType);
            }
            catch (Exception e)
            {
                if (e is FormatException ||
                    e is ArgumentNullException ||
                    e is OverflowException)
                {
                    throw new ArgumentException($@"Parameter ""{param}"" cannot be resolved as stored procedure parameter ""{procParamName}"" " +
                        $@"with type ""{systemType.Name}"".");
                }

                throw;
            }
        }
    }
}
