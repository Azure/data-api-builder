// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// <param name="operationType">Action to provide the authorizationResolver during policy lookup.</param>
        /// <param name="queryStructure">SqlQueryStructure object, could be a subQueryStructure which is of the same type.</param>
        /// <param name="context">The GraphQL Middleware context with request metadata like HttpContext.</param>
        /// <param name="authorizationResolver">Used to lookup authorization policies.</param>
        /// <param name="sqlMetadataProvider">Provides helper method to process ODataFilterClause.</param>
        public static void ProcessAuthorizationPolicies(
            EntityActionOperation operationType,
            BaseQueryStructure queryStructure,
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
            List<EntityActionOperation>? elementalOperations = ResolveCompoundOperationToElementalOperations(operationType);

            Dictionary<string, DatabaseObject> entitiesToProcess = new();
            if (queryStructure is BaseSqlQueryStructure baseSqlQueryStructure)
            {
                ProcessFilter(
                    context: context,
                    authorizationResolver: authorizationResolver,
                    sqlMetadataProvider: sqlMetadataProvider,
                    clientRoleHeader: clientRoleHeader,
                    elementalOperations: elementalOperations,
                    entityName: queryStructure.EntityName,
                    entityDBObject: queryStructure.DatabaseObject,
                    postProcessCallback: (filterClause, elementalOperation) =>
                    {
                        baseSqlQueryStructure.ProcessOdataClause(filterClause, elementalOperation);
                    });
                ;
            }
            else if (sqlMetadataProvider is CosmosSqlMetadataProvider cosmosSqlMetadataProvider &&
                queryStructure is CosmosQueryStructure cosmosQueryStructure)
            {
                Dictionary<string, List<EntityDbPolicyCosmosModel>> entityPaths = cosmosSqlMetadataProvider.EntityWithJoins;

                foreach (KeyValuePair<string, List<EntityDbPolicyCosmosModel>> entity in entityPaths)
                {
                    ProcessFilter(
                        context: context,
                        authorizationResolver: authorizationResolver,
                        sqlMetadataProvider: cosmosSqlMetadataProvider,
                        clientRoleHeader: clientRoleHeader,
                        elementalOperations: elementalOperations,
                        entityName: entity.Key,
                        entityDBObject: null,
                        postProcessCallback: (filterClause, _) =>
                        {
                            if (filterClause is null)
                            {
                                return;
                            }

                            foreach (EntityDbPolicyCosmosModel pathConfig in entity.Value)
                            {
                                string? existQuery = null;
                                string? fromClause = string.Empty;
                                string? predicates = string.Empty;

                                if (pathConfig.Alias is not null)
                                {
                                    if (pathConfig.ColumnName is null || pathConfig.EntityName is null)
                                    {
                                        continue;
                                    }
                                    //Increment Table counter with the new JOIN so that we can have unique alias for each join
                                    cosmosQueryStructure.TableCounter.Next();

                                    fromClause = pathConfig.JoinStatement;
                                    predicates = filterClause?.Expression.Accept(new ODataASTCosmosVisitor(pathConfig.Alias));
                                }
                                else
                                {
                                    fromClause = $"{pathConfig.Path}.{pathConfig.ColumnName}";
                                    predicates = filterClause?.Expression.Accept(new ODataASTCosmosVisitor($"{pathConfig.Path}.{pathConfig.ColumnName}"));
                                }

                                existQuery = GetExistQueryForCosmos(fromClause, predicates);

                                if (pathConfig.EntityName == entity.Key)
                                {
                                    if (!cosmosQueryStructure.DbPolicyPredicatesForOperations.TryGetValue(operationType, out string? _))
                                    {
                                        cosmosQueryStructure.DbPolicyPredicatesForOperations[operationType]
                                                        = existQuery;
                                    }
                                    else
                                    {
                                        cosmosQueryStructure.DbPolicyPredicatesForOperations[operationType]
                                                        += $" AND {existQuery}";
                                    }
                                }
                            }
                        });
                }
            }
        }

        /// <summary>
        /// Generate Cosmos DB Query for the given fromClause and predicates.
        /// </summary>
        /// <param name="fromClause"></param>
        /// <param name="predicates"></param>
        /// <returns></returns>
        private static string GetExistQueryForCosmos(string? fromClause, string? predicates)
        {
            string? existQuery = $"EXISTS " +
                                $"(SELECT VALUE 1 " +
                                    $"FROM {fromClause} ";
            if (predicates is not null)
            {
                existQuery += $"WHERE {predicates})";
            }
            else
            {
                existQuery += ")";
            }

            return existQuery;
        }

        /// <summary>
        /// It process the filter clauses for the given elemental operation.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="authorizationResolver"></param>
        /// <param name="sqlMetadataProvider"></param>
        /// <param name="clientRoleHeader"></param>
        /// <param name="elementalOperations"></param>
        /// <param name="entityName"></param>
        /// <param name="entityDBObject"></param>
        /// <param name="postProcessCallback"></param>
        /// <returns></returns>
        private static List<FilterClause> ProcessFilter(HttpContext context,
            IAuthorizationResolver authorizationResolver,
            ISqlMetadataProvider sqlMetadataProvider,
            string clientRoleHeader,
            List<EntityActionOperation> elementalOperations,
            string entityName,
            DatabaseObject? entityDBObject,
            Action<FilterClause?, EntityActionOperation> postProcessCallback)
        {
            List<FilterClause> filterClauses = new();
            foreach (EntityActionOperation elementalOperation in elementalOperations)
            {
                string dbQueryPolicy = authorizationResolver.ProcessDBPolicy(
                entityName,
                clientRoleHeader,
                elementalOperation,
                context);

                FilterClause? filterClause = GetDBPolicyClauseForQueryStructure(
                    dbQueryPolicy,
                    entityName: entityName,
                    resourcePath: (entityDBObject is not null) ? $"{entityName}.{entityDBObject.FullName}" : entityName,
                    sqlMetadataProvider);

                postProcessCallback(filterClause, elementalOperation);
            }

            return filterClauses;
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
                    resourcePath: resourcePath,
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
    }
}
