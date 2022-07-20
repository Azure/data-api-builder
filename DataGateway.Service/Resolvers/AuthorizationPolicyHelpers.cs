using Azure.DataGateway.Auth;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.OData.UriParser;

namespace Azure.DataGateway.Service.Resolvers
{
    public static class AuthorizationPolicyHelpers
    {
        /// <summary>
        /// Retrieves the Database Authorization Policiy from the AuthorizationResolver
        /// and converts it into a dbQueryPolicy string to be processed by the OData Visitor helpers.
        /// As a result, all top level and sub-queries will have a database query predicate string
        /// on the SqlQueryStructure, if policies are applicable.
        /// </summary>
        /// <param name="actionType">Action to provide the authorizationResolver during policy lookup.</param>
        /// <param name="queryStructure">SqlQueryStructure object, could be a subQueryStucture which is of the same type.</param>
        /// <param name="context">The GraphQL Middleware context with request metadata like HttpContext.</param>
        /// <param name="authorizationResolver">Used to lookup authorization policies.</param>
        /// <param name="sqlMetadataProvider">Provides helper method to process ODataFilterClause.</param>
        public static void ProcessAuthorizationPolicies(
            string actionType,
            SqlQueryStructure queryStructure,
            HttpContext context,
            IAuthorizationResolver authorizationResolver,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            if (!context.Request.Headers.TryGetValue(AuthorizationResolver.CLIENT_ROLE_HEADER, out StringValues roleHeaderValue))
            {
                throw new DataGatewayException(
                    message: "No ClientRoleHeader found in GraphQL Middleware Context.",
                    statusCode: System.Net.HttpStatusCode.Forbidden,
                    subStatusCode: DataGatewayException.SubStatusCodes.AuthorizationCheckFailed);
            }

            string clientRoleHeader = roleHeaderValue.ToString();

            string dbQueryPolicy = authorizationResolver.TryProcessDBPolicy(
                queryStructure.EntityName,
                clientRoleHeader,
                actionType,
                context);

            FilterClause? filterClause = GetDBPolicyClauseForQueryStructure(
                dbQueryPolicy,
                entityName: queryStructure.EntityName,
                resourcePath: queryStructure.DatabaseObject.FullName,
                sqlMetadataProvider);

            if (filterClause is null)
            {
                return;
            }

            queryStructure.ProcessDBPolicyClause(filterClause);
        }

        /// <summary>
        /// Given a dbPolicyClause string, appends the string formatting needed to be processed by ODataFilterParser
        /// As a result each queryStructure object passing through this function will have database query predicates created
        /// for use by the SqlQueryBuilder.
        /// </summary>
        /// <param name="dbPolicyClause">string representation of a processed database authorization policy.</param>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="resourcePath">Name of the schema. i.e. dbo</param>
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
                // This helps the ODataVisitor helpers to parse the policy text properly.
                dbPolicyClause = "?$filter=" + dbPolicyClause;

                // Parse and save the values that are needed to later generate SQL query predicates
                // FilterClauseInDbPolicy is an Abstract Syntax Tree representing the parsed policy text.
                return sqlMetadataProvider.GetODataFilterParser().GetFilterClause(dbPolicyClause, $"{entityName}.{resourcePath}");
            }

            return null;
        }
    }
}
