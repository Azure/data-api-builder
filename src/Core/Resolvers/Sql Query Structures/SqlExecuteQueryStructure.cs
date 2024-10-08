// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;

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
                    string? parameterizedName;
                    if (requestParamValue is not null)
                    {
                        parameterizedName = MakeDbConnectionParam(GetParamAsSystemType(requestParamValue.ToString()!, paramKey, systemType), paramKey);
                    }
                    else
                    {
                        parameterizedName = MakeDbConnectionParam(value: null, paramKey);
                    }

                    ProcedureParameters.Add(paramKey, $"{parameterizedName}");
                }
                else if (paramDefinition.HasConfigDefault)
                {
                    // When the runtime config defines a default value for a parameter,
                    // create a parameter using that value and add it to the query structure.
                    // Database metadata does not indicate whether a SP parameter has a default value
                    // and as a result, we don't know whether an SP parameter is optional.
                    // Therefore, DAB relies on the database error to indicate that a required parameter is missing.
                    object? value = paramDefinition.ConfigDefaultValue == null ? null : GetParamAsSystemType(paramDefinition.ConfigDefaultValue!.ToString()!, paramKey, systemType);
                    string parameterizedName = MakeDbConnectionParam(value, paramKey);
                    ProcedureParameters.Add(paramKey, $"{parameterizedName}");
                }
            }
        }
    }
}
