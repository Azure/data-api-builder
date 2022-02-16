using System;
using System.IO;
using System.Net;
using System.Text;
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
        public async Task<JsonDocument?> ExecuteAsync(
            string entityName,
            Operation operationType,
            string primaryKeyRoute)
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
                    RequestValidator.ValidateInsertRequestContext(
                        (InsertRequestContext)context,
                        _metadataStoreProvider);
                    break;
                case Operation.Delete:
                    context = new DeleteRequestContext(entityName, isList: false);
                    RequestValidator.ValidateDeleteRequest(primaryKeyRoute);
                    break;
                case Operation.Upsert:
                    JsonElement upsertPayloadRoot = RequestValidator.ValidateUpsertRequest(primaryKeyRoute, requestBody);
                    context = new UpsertRequestContext(entityName, upsertPayloadRoot, HttpRestVerbs.PUT, operationType);
                    RequestValidator.ValidateUpsertRequestContext((UpsertRequestContext)context, _metadataStoreProvider);
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
                RequestParser.ParseQueryString(HttpUtility.ParseQueryString(queryString), context, _metadataStoreProvider.GetFilterParser());
            }

            // At this point for DELETE, the primary key should be populated in the Request context. 
            RequestValidator.ValidateRequestContext(context, _metadataStoreProvider);

            // RestRequestContext is finalized for QueryBuilding and QueryExecution.
            // Perform Authorization check prior to moving forward in request pipeline.
            // RESTAuthorizationService
            AuthorizationResult authorizationResult = await _authorizationService.AuthorizeAsync(
                user: GetHttpContext().User,
                resource: context,
                requirements: new[] { context.HttpVerb });

            if (authorizationResult.Succeeded)
            {
                switch (operationType)
                {
                    case Operation.Find:
                        return await _queryEngine.ExecuteAsync(context);
                    case Operation.Insert:
                    case Operation.Delete:
                    case Operation.Upsert:
                        return await _mutationEngine.ExecuteAsync(context);
                    default:
                        throw new NotSupportedException("This operation is not yet supported.");
                };
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
        /// For the given entity, constructs the primary key route
        /// using the primary key names from metadata and their values from the JsonElement
        /// representing one instance of the entity.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="entity">A Json element representing one instance of the entity.</param>
        /// <remarks> This function expects the Json element entity to contain all the properties
        /// that make up the primary keys.</remarks>
        /// <returns>the primary key route e.g. /id/1/partition/2 where id and partition are primary keys.</returns>
        public string ConstructPrimaryKeyRoute(string entityName, JsonElement entity)
        {
            TableDefinition tableDefinition = _metadataStoreProvider.GetTableDefinition(entityName);
            StringBuilder newPrimaryKeyRoute = new();

            foreach (string primaryKey in tableDefinition.PrimaryKey)
            {
                newPrimaryKeyRoute.Append(primaryKey);
                newPrimaryKeyRoute.Append("/");
                newPrimaryKeyRoute.Append(entity.GetProperty(primaryKey).ToString());
                newPrimaryKeyRoute.Append("/");
            }

            // Remove the trailing "/"
            newPrimaryKeyRoute.Remove(newPrimaryKeyRoute.Length - 1, 1);

            return newPrimaryKeyRoute.ToString();
        }

        private HttpContext GetHttpContext()
        {
            return _httpContextAccessor.HttpContext!;
        }
    }
}
