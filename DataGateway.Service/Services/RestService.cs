using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Services
{
    /// <summary>
    /// Service providing REST Api executions.
    /// </summary>
    public class RestService
    {
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;
        private readonly IMetadataStoreProvider _metadataStoreProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAuthorizationService _authorizationService;

        public RestService(
            IQueryEngine queryEngine,
            IMutationEngine mutationEngine,
            IMetadataStoreProvider metadataStoreProvider,
            IHttpContextAccessor httpContextAccessor,
            IAuthorizationService authorizationService
            )
        {
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _metadataStoreProvider = metadataStoreProvider;
            _httpContextAccessor = httpContextAccessor;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Invokes the request parser to identify major components of the FindRequestContext
        /// and executes the find query.
        /// </summary>
        /// <param name="entityName">The entity name.</param>
        /// <param name="primaryKeyRoute">The primary key route. e.g. customerName/Xyz/saleOrderId/123</param>
        /// <param name="queryString">The query string portion of the request. e.g. ?_f=customerName</param>
        public async Task<JsonDocument> ExecuteFindAsync(string entityName, string primaryKeyRoute)
        {
            string queryString = _httpContextAccessor.HttpContext.Request.QueryString.ToString();

            FindRequestContext context = new(entityName, isList: string.IsNullOrEmpty(primaryKeyRoute));
            RequestParser.ParsePrimaryKey(primaryKeyRoute, context);

            if (!string.IsNullOrEmpty(queryString))
            {
                RequestParser.ParseQueryString(System.Web.HttpUtility.ParseQueryString(queryString), context);
            }

            RequestValidator.ValidateFindRequest(context, _metadataStoreProvider);

            // RequestContext is finalized for QueryBuilding and QueryExecution.
            // Perform Authorization check prior to moving forward in request pipeline.
            // RESTAuthorizationService
            AuthorizationResult authorizationResult = await _authorizationService.AuthorizeAsync(
                user: _httpContextAccessor.HttpContext.User,
                resource: context,
                requirements: new[] { HttpRestVerbs.GET });

            if (authorizationResult.Succeeded)
            {
                return await _queryEngine.ExecuteAsync(context);
            }
            else
            {
                throw new DatagatewayException(
                    message: "Unauthorized",
                    statusCode: (int)HttpStatusCode.Unauthorized,
                    subStatusCode: DatagatewayException.SubStatusCodes.AuthorizationCheckFailed
                );
            }
        }

        /// <summary>
        /// Invokes the request parser to identify major components of the InsertRequestContext
        /// and executes the insert operation.
        /// </summary>
        /// <param name="entityName">The entity name.</param>
        public async Task<JsonDocument> ExecuteInsertAsync(string entityName, string requestBody)
        {
            string queryString = _httpContextAccessor.HttpContext.Request.QueryString.ToString();

            JsonElement insertPayloadRoot = RequestValidator.ValidatePostRequest(queryString, requestBody);

            InsertRequestContext context = new(entityName,
                insertPayloadRoot,
                HttpRestVerbs.POST,
                Operation.Insert);

            RequestValidator.ValidateRequestContext(context, _metadataStoreProvider);

            // RequestContext is finalized for QueryBuilding and QueryExecution.
            // Perform Authorization check prior to moving forward in request pipeline.
            // RESTAuthorizationService
            AuthorizationResult authorizationResult = await _authorizationService.AuthorizeAsync(
                user: _httpContextAccessor.HttpContext.User,
                resource: context,
                requirements: new[] { HttpRestVerbs.POST });

            if (authorizationResult.Succeeded)
            {
                return await _mutationEngine.ExecuteAsync(context);
            }
            else
            {
                throw new DatagatewayException(
                    message: "Unauthorized",
                    statusCode: (int)HttpStatusCode.Unauthorized,
                    subStatusCode: DatagatewayException.SubStatusCodes.AuthorizationCheckFailed
                );
            }
        }
    }
}
