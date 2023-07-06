// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    ///<summary>
    /// Wraps all the required data and logic to write a SQL EXECUTE query
    ///</summary>
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
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            IDictionary<string, object?> requestParams)
        : base(sqlMetadataProvider, authorizationResolver, gQLFilterParser, entityName: entityName)
        {
            StoredProcedureDefinition storedProcedureDefinition = GetUnderlyingStoredProcedureDefinition();
            ProcedureParameters = new();
            foreach ((string paramKey, ParameterDefinition paramDefinition) in storedProcedureDefinition.Parameters)
            {
                Type systemType = GetUnderlyingStoredProcedureDefinition().Parameters[paramKey].SystemType!;
                // Populate with request param if able
                if (requestParams.TryGetValue(paramKey, out object? requestParamValue))
                {
                    // Parameterize, then add referencing parameter to ProcedureParameters dictionary
                    string? parametrizedName = null;
                    if (requestParamValue is not null)
                    {
                        parametrizedName = MakeDbConnectionParam(GetParamAsSystemType(requestParamValue.ToString()!, paramKey, systemType), paramKey);
                    }
                    else
                    {
                        parametrizedName = MakeDbConnectionParam(value: null, paramKey);
                    }

                    ProcedureParameters.Add(paramKey, $"{parametrizedName}");
                }
                else
                {
                    // Fill with default value from runtime config
                    if (paramDefinition.HasConfigDefault)
                    {
                        object? value = paramDefinition.ConfigDefaultValue == null ? null : GetParamAsSystemType(paramDefinition.ConfigDefaultValue!.ToString()!, paramKey, systemType);
                        string parameterizedName = MakeDbConnectionParam(value, paramKey);
                        ProcedureParameters.Add(paramKey, $"{parameterizedName}");
                    }
                    else
                    {
                        // In case required parameters not found in request and no default specified in config
                        // Should already be handled in the request validation step
                        throw new DataApiBuilderException(message: $"Did not provide all procedure params, missing: \"{paramKey}\"",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }
                }
            }
        }
    }
}
