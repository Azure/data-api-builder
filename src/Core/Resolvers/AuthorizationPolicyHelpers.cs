// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Helpers that enable processing database authorization policies
    /// defined in the runtime config for SQL queries.
    /// </summary>
    public static class AuthorizationPolicyHelpers
    {
        /// <summary>
        /// Retrieves the Database Authorization Policy from the AuthorizationResolver
        /// and converts it into a dbQueryPolicy string.
        /// Then, the OData clause is processed for the passed in SqlQueryStructure
        /// by calling OData visitor helpers.
        /// </summary>
        /// <param name="operation">Action to provide the authorizationResolver during policy lookup.</param>
        /// <param name="queryStructure">SqlQueryStructure object, could be a subQueryStructure which is of the same type.</param>
        /// <param name="context">The GraphQL Middleware context with request metadata like HttpContext.</param>
        /// <param name="authorizationResolver">Used to lookup authorization policies.</param>
        /// <param name="sqlMetadataProvider">Provides helper method to process ODataFilterClause.</param>
        public static void ProcessAuthorizationPolicies(
            EntityActionOperation operationType,
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
            List<EntityActionOperation> elementalOperations = ResolveCompoundOperationToElementalOperations(operationType);

            foreach (EntityActionOperation elementalOperation in elementalOperations)
            {
                string dbQueryPolicy = authorizationResolver.ProcessDBPolicy(
                    queryStructure.EntityName,
                    clientRoleHeader,
                    elementalOperation,
                    context);

                if(string.IsNullOrEmpty(dbQueryPolicy))
                {
                    continue;
                }

                FilterClause? filterClause = GetDBPolicyClauseForQueryStructure(
                    dbQueryPolicy,
                    entityName: queryStructure.EntityName,
                    resourcePath: queryStructure.DatabaseObject.FullName,
                    sqlMetadataProvider);
                queryStructure.ProcessOdataClause(filterClause, elementalOperation);
            }
        }

        public static void ProcessAuthorizationPolicies(
          EntityActionOperation operationType,
          HttpContext context,
          IAuthorizationResolver authorizationResolver,
          CosmosSqlMetadataProvider sqlMetadataProvider,
          CosmosQueryStructure cosmosQueryStructure,
          IDictionary<string, object> parameters)
        {
            if (!context.Request.Headers.TryGetValue(AuthorizationResolver.CLIENT_ROLE_HEADER, out StringValues roleHeaderValue))
            {
                throw new DataApiBuilderException(
                    message: "No ClientRoleHeader found in request context.",
                    statusCode: System.Net.HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
            }

            List<FilterClause?> filterClauseList = new ();
            foreach ((string entityName, DatabaseObject _) in sqlMetadataProvider.GetEntityNamesAndDbObjects())
            {
                string clientRoleHeader = roleHeaderValue.ToString();

                string dbQueryPolicy = authorizationResolver.ProcessDBPolicy(
                    entityName,
                    clientRoleHeader,
                    operationType,
                    context);

                if (string.IsNullOrEmpty(dbQueryPolicy))
                {
                    continue;
                }

                FilterClause? filterClause = GetDBPolicyClauseForQueryStructure(
                    dbQueryPolicy,
                    entityName: entityName,
                    resourcePath: null,
                    sqlMetadataProvider);

                filterClauseList.Add(filterClause);
            }

            ProcessOdataClause(sqlMetadataProvider, filterClauseList, parameters, operationType, cosmosQueryStructure);
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
            string? resourcePath,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            if (!string.IsNullOrEmpty(dbPolicyClause))
            {
                // Since dbPolicy is nothing but filters to be added by virtue of database policy, we prefix it with
                // ?$filter= so that it conforms with the format followed by other filter predicates.
                // This enables the ODataVisitor helpers to parse the policy text properly.
                dbPolicyClause = $"?{RequestParser.FILTER_URL}={dbPolicyClause}";

                string fullResourcePath = entityName;
                if(!string.IsNullOrEmpty(resourcePath))
                {
                    fullResourcePath = $"{entityName}.{resourcePath}";
                }

                // Parse and save the values that are needed to later generate SQL query predicates
                // FilterClauseInDbPolicy is an Abstract Syntax Tree representing the parsed policy text.
                return sqlMetadataProvider.GetODataParser().GetFilterClause(
                    filterQueryString: dbPolicyClause,
                    resourcePath: fullResourcePath,
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
        private static List<EntityActionOperation> ResolveCompoundOperationToElementalOperations(EntityActionOperation operation)
        {
            return operation switch
            {
                EntityActionOperation.Upsert or
                EntityActionOperation.UpsertIncremental =>
                    new List<EntityActionOperation> { EntityActionOperation.Update, EntityActionOperation.Create },
                _ => new List<EntityActionOperation> { operation },
            };
        }

        /// <summary>
        /// After SqlQueryStructure is instantiated, process a database authorization policy
        /// for GraphQL requests with the ODataASTVisitor to populate DbPolicyPredicates.
        /// Processing will also occur for GraphQL sub-queries.
        /// </summary>
        /// <param name="dbPolicyClause">FilterClause from processed runtime configuration permissions Policy:Database</param>
        /// <param name="operation">CRUD operation for which the database policy predicates are to be evaluated.</param>
        /// <exception cref="DataApiBuilderException">Thrown when the OData visitor traversal fails. Possibly due to malformed clause.</exception>
        public static void ProcessOdataClause(
            CosmosSqlMetadataProvider sqlMetadataProvider,
            List<FilterClause?> dbPolicyClauseList,
            IDictionary<string, object> parameters,
            EntityActionOperation operationType,
            CosmosQueryStructure cosmosQueryStructure)
        {
            string? conditionForWhereClause = null;
            try
            {
                foreach (FilterClause? filterClause in dbPolicyClauseList)
                {
                    string? entityName = filterClause?.ItemType.Definition.ToString();
                    if (filterClause is null || entityName is null)
                    {
                        continue;
                    }

                    ODataASTVisitor visitor = new(cosmosQueryStructure, sqlMetadataProvider, operationType);

                    if (string.IsNullOrEmpty(conditionForWhereClause))
                    {
                        conditionForWhereClause = GetFilterPredicatesFromOdataClause(filterClause, visitor);
                    }
                    else
                    {
                        conditionForWhereClause += " AND " + GetFilterPredicatesFromOdataClause(filterClause, visitor);
                    }
                }

                cosmosQueryStructure.DbPolicyPredicatesForOperations[operationType] = conditionForWhereClause;
            }
            catch (Exception ex)
            {
                throw new DataApiBuilderException(
                    message: "Policy query parameter is not well formed for GraphQL Policy Processing.",
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed,
                    innerException: ex);
            }
        }

        private static string? GetFilterPredicatesFromOdataClause(FilterClause filterClause, ODataASTVisitor visitor)
        {
            return filterClause.Expression.Accept<string>(visitor);
        }
    }
}
