// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Parsers;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Helpers that enable processing database authorization policies
    /// defined in the runtime config for SQL queries.
    /// </summary>
    public static class AuthorizationPolicyHelpers
    {
        /// <summary>
        /// Retrieves the Database Authorization Policiy from the AuthorizationResolver
        /// and converts it into a dbQueryPolicy string.
        /// Then, the OData clause is processed for the passed in SqlQueryStructure
        /// by calling OData visitor helpers.
        /// </summary>
        /// <param name="operationType">Action to provide the authorizationResolver during policy lookup.</param>
        /// <param name="queryStructure">SqlQueryStructure object, could be a subQueryStucture which is of the same type.</param>
        /// <param name="context">The GraphQL Middleware context with request metadata like HttpContext.</param>
        /// <param name="authorizationResolver">Used to lookup authorization policies.</param>
        /// <param name="sqlMetadataProvider">Provides helper method to process ODataFilterClause.</param>
        public static void ProcessAuthorizationPolicies(
            Config.Operation operationType,
            BaseSqlQueryStructure queryStructure,
            HttpContext context,
            IAuthorizationResolver authorizationResolver,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            if (!context.Request.Headers.TryGetValue(AuthorizationResolver.CLIENT_ROLE_HEADER, out StringValues roleHeaderValue))
            {
                throw new DataApiBuilderException(
                    message: "No ClientRoleHeader found in request context.",
                    statusCode: System.Net.HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
            }

            string clientRoleHeader = roleHeaderValue.ToString();
            List<Config.Operation> elementalOperations = ResolveCompoundOperationToElementalOperations(operation: operationType);
            ODataASTVisitor visitor = new(queryStructure, sqlMetadataProvider);

            // Add the database policy predicates for each of the constituent operations.
            foreach (Config.Operation elementalOperation in elementalOperations)
            {
                string dbQueryPolicy = authorizationResolver.ProcessDBPolicy(
                queryStructure.EntityName,
                clientRoleHeader,
                elementalOperation,
                context);

                FilterClause? filterClause = GetDBPolicyClauseForQueryStructure(
                    dbQueryPolicy,
                    entityName: queryStructure.EntityName,
                    resourcePath: queryStructure.DatabaseObject.FullName,
                    sqlMetadataProvider);

                // Update the operation for which the oData clause is being processed.
                visitor.operation = elementalOperation;
                queryStructure.ProcessOdataClause(filterClause, elementalOperation, visitor);
            }
        }

        /// <summary>
        /// Given a dbPolicyClause string, appends the string formatting needed to be processed by ODataParser
        /// </summary>
        /// <param name="dbPolicyClause">string representation of a processed database authorization policy.</param>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="resourcePath">Name of the schema. e.g. `dbo` for MsSql.</param>
        /// <param name="sqlMetadataProvider">Provides helper method to process ODataFilterClause.</param>
        public static FilterClause? GetDBPolicyClauseForQueryStructure(
            string dbPolicyClause,
            string entityName,
            string resourcePath,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            if (!string.IsNullOrEmpty(dbPolicyClause))
            {
                // Since dbPolicy is nothing but filters to be added by virtue of database policy, we prefix it with
                // ?$filter= so that it conforms with the format followed by other filter predicates.
                // This enables the ODataVisitor helpers to parse the policy text properly.
                dbPolicyClause = $"?{RequestParser.FILTER_URL}={dbPolicyClause}";

                // Parse and save the values that are needed to later generate SQL query predicates
                // FilterClauseInDbPolicy is an Abstract Syntax Tree representing the parsed policy text.
                return sqlMetadataProvider.GetODataParser().GetFilterClause(
                    filterQueryString: dbPolicyClause,
                    resourcePath: $"{entityName}.{resourcePath}",
                    customResolver: new ClaimsTypeDataUriResolver());
            }

            return null;
        }

        /// <summary>
        /// Resolves compound operations like Upsert,UpsertIncremental into the corresponding constituent elemental operations i.e. Create,Update.
        /// For simple operations, returns the operation itself.
        /// </summary>
        /// <param name="operation">Operation to be resolved.</param>
        /// <returns>Constituent operations for the operation.</returns>
        private static List<Config.Operation> ResolveCompoundOperationToElementalOperations(Config.Operation operation)
        {
            switch (operation)
            {
                case Config.Operation.Upsert:
                case Config.Operation.UpsertIncremental:
                    return new List<Config.Operation> { Config.Operation.Update, Config.Operation.Create };
                default:
                    return new List<Config.Operation> { operation };
            }
        }
    }
}
