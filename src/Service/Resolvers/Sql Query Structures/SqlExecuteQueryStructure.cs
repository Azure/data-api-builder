// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Defines a parameter for an EXECUTE stored procedure call.
    /// </summary>
    public class SqlExecuteParameter
    {
        /// <summary>
        /// The name of the parameter
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The engine-generated referencing parameters (e.g. @param0, @param1...)
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// The direction of the parameter (e.g. Input, Output, InputOutput)
        /// </summary>
        public ParameterDirection Direction { get; set; }

        /// <summary>
        /// Whether the parameter is an output parameter.
        /// </summary>
        public bool IsOutput
        {
            get
            {
                return this.Direction == System.Data.ParameterDirection.Output
                    || this.Direction == System.Data.ParameterDirection.InputOutput;
            }
        }

        /// <summary>
        /// Constructs a parameter with the given name and direction.
        /// </summary>
        /// <param name="value">The engine-generated referencing parameters (e.g. @param0, @param1...)/param>
        /// <param name="direction">The direction of the parameter (e.g. Input, Output, InputOutput)</param>
        public SqlExecuteParameter(string name, object? value, ParameterDirection direction)
        {
            Name = name;
            Value = value;
            Direction = direction;
        }
    }

    ///<summary>
    /// Wraps all the required data and logic to write a SQL EXECUTE query
    ///</summary>
    public class SqlExecuteStructure : BaseSqlQueryStructure
    {
        // Holds the final, resolved parameters that will be passed when building the execute stored procedure query
        // Keys are the user-generated procedure parameter names
        public Dictionary<string, object> ProcedureParameters { get; set; }

        // Whether this SqlExecuteQueryStructure has any OUTPUT parameters.
        public bool HasOutputParameters
        {
            get
            {
                return Parameters.Values.Any(p => p is SqlExecuteParameter executeParameter && executeParameter.IsOutput);
            }
        }

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
                var parameterDirection = paramDefinition.Direction ?? ParameterDirection.Input;

                // Populate with request param if able
                if (requestParams.TryGetValue(paramKey, out object? requestParamValue))
                {
                    // Parameterize, then add referencing parameter to ProcedureParameters dictionary
                    string? parametrizedName = null;
                    if (requestParamValue is not null)
                    {
                        Type systemType = GetUnderlyingStoredProcedureDefinition().Parameters[paramKey].SystemType!;
                        parametrizedName = MakeParamWithValue(
                            new SqlExecuteParameter(
                                paramKey,
                                GetParamAsSystemType(
                                    requestParamValue.ToString()!,
                                    paramKey,
                                    systemType
                                ), parameterDirection
                            )
                        );
                    }
                    else
                    {
                        parametrizedName = MakeParamWithValue(value: null);
                    }

                    ProcedureParameters.Add(paramKey, $"{parametrizedName}");
                }
                else
                {
                    // Fill with default value from runtime config
                    if (paramDefinition.HasConfigDefault)
                    {
                        string parameterizedName = MakeParamWithValue(
                            new SqlExecuteParameter(
                                paramKey,
                                paramDefinition.ConfigDefaultValue,
                                parameterDirection
                            )
                        );
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
