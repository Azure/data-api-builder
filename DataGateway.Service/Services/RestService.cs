using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

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
        /// Invokes the request parser to identify major components of the RestRequestContext
        /// and executes the given operation.
        /// </summary>
        /// <param name="entityName">The entity name.</param>
        /// <param name="operationType">The kind of operation to execute.</param>
        /// <param name="primaryKeyRoute">The primary key route. e.g. customerName/Xyz/saleOrderId/123</param>
        public async Task<JsonDocument> ExecuteAsync(
            string    entityName,
            Operation operationType,
            string    primaryKeyRoute)
        {
            string queryString = GetHttpContext().Request.QueryString.ToString();

            string requestBody = string.Empty;
            using (StreamReader reader = new(GetHttpContext().Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            RestRequestContext context;
            switch (operationType)
            {
                case Operation.Find:
                     context = new FindRequestContext(entityName, isList: string.IsNullOrEmpty(primaryKeyRoute));
                     break;
                case Operation.Insert:
                    JsonElement insertPayloadRoot = RequestValidator.ValidateInsertRequest(queryString, requestBody);
                    context = new InsertRequestContext(entityName,
                        insertPayloadRoot,
                        HttpRestVerbs.POST,
                        operationType);
                    break;
                case Operation.Delete:
                    context = new DeleteRequestContext(entityName, isList: false);
                    break;
                default:
                    throw new NotSupportedException("This operation is not yet supported.");
            }

            if (!string.IsNullOrEmpty(primaryKeyRoute))
            {
                // After parsing primary key, the context will be populated with the
                // correct PrimaryKeyValuePairs.
                RequestParser.ParsePrimaryKey(primaryKeyRoute, context);
                RequestValidator.ValidatePrimaryKey(context, _metadataStoreProvider);
            }

            if (!string.IsNullOrEmpty(queryString))
            {
                RequestParser.ParseQueryString(HttpUtility.ParseQueryString(queryString), context);
            }

            //at this point for DELETE, the primary key should be populated in teh Request context. 
            RequestValidator.ValidateRequestContext(context, _metadataStoreProvider);

            // RestRequestContext is finalized for QueryBuilding and QueryExecution.
            // Perform Authorization check prior to moving forward in request pipeline.
            // RESTAuthorizationService
            AuthorizationResult authorizationResult = await _authorizationService.AuthorizeAsync(
                user: _httpContextAccessor.HttpContext.User,
                resource: context,
                requirements: new[] { context.HttpVerb });

            if (authorizationResult.Succeeded)
            {
                switch (operationType)
                {
                    case Operation.Find:
                        return await _queryEngine.ExecuteAsync(context);
                    case Operation.Insert:
                        return await _mutationEngine.ExecuteAsync(context);
                    case Operation.Delete:
                        return await _mutationEngine.ExecuteAsync(context);
                    default:
                        throw new NotSupportedException("This operation is not yet supported.");
                }
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

        private HttpContext GetHttpContext()
        {
            return _httpContextAccessor.HttpContext;
        }
    }
}
