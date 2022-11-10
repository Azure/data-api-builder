using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Represents the query used for an EXISTS clause.
    /// e.g.
    /// EXISTS (
    /// SELECT 1
    /// FROM <sourcename>
    /// AS [table1]
    /// WHERE[table1].[column] = <value>
    /// )
    /// </summary>
    public class SqlExistsQueryStructure: BaseSqlQueryStructure
    {
        /// <summary>
        /// Constructor for Exists query.
        /// </summary>
        /// <exception cref="DataApiBuilderException">if middleware context doesn't have an httpcontext</exception>
        public SqlExistsQueryStructure(
            IMiddlewareContext ctx,
            ISqlMetadataProvider metadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            List<Predicate> predicates,
            string entityName,
            IncrementingInteger? counter = null)
            : base(metadataProvider, authorizationResolver, gQLFilterParser, predicates, entityName, counter)
        {
            // Get HttpContext from IMiddlewareContext and fail if resolved value is null.
            if (!ctx.ContextData.TryGetValue(nameof(HttpContext), out object? httpContextValue))
            {
                throw new DataApiBuilderException(
                    message: "No HttpContext found in GraphQL Middleware Context.",
                    statusCode: System.Net.HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
            }

            HttpContext httpContext = (HttpContext)httpContextValue!;

            // This adds any required DBPolicyPredicates to this Exists query structure.
            AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                Operation.Read,
                queryStructure: this,
                httpContext,
                authorizationResolver,
                metadataProvider);
        }
    }
}
