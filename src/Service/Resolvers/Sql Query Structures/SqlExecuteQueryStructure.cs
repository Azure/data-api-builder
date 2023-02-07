// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;

namespace Azure.DataApiBuilder.Service.Resolvers
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
                // Populate with request param if able
                if (requestParams.TryGetValue(paramKey, out object? requestParamValue))
                {
                    // Parameterize, then add referencing parameter to ProcedureParameters dictionary
                    try
                    {
                        string parameterizedName = MakeParamWithValue(requestParamValue is null ? null :
                            GetParamAsProcedureParameterType(requestParamValue.ToString()!, paramKey));
                        ProcedureParameters.Add(paramKey, $"@{parameterizedName}");
                    }
                    catch (ArgumentException ex)
                    {
                        // In the case GetParamAsProcedureParameterType fails to parse as SystemType from database metadata
                        // Keep message being returned to the client more generalized to not expose schema info
                        throw new DataApiBuilderException(
                            message: $"Invalid value supplied for field: {paramKey}",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                            innerException: ex);
                    }
                }
                else
                {
                    // Fill with default value from runtime config
                    if (paramDefinition.HasConfigDefault)
                    {
                        string parameterizedName = MakeParamWithValue(paramDefinition.ConfigDefaultValue);
                        ProcedureParameters.Add(paramKey, $"@{parameterizedName}");
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

        /// <summary>
        /// Gets the value of the parameter cast as the system type
        /// of the stored procedure parameter this parameter is associated with
        /// </summary>
        private object GetParamAsProcedureParameterType(string param, string procParamName)
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
                        $@"with type ""{systemType.Name}"".", innerException: e);
                }

                throw;
            }
        }
    }
}
