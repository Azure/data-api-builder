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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// Service providing REST Api executions.
    /// </summary>
    public class RestService
    {
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAuthorizationService _authorizationService;
        public IMetadataStoreProvider MetadataStoreProvider { get; }
        public RestRequestContext Context { get; set; }
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
            MetadataStoreProvider = metadataStoreProvider;
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
            string? primaryKeyRoute)
        {
            string queryString = GetHttpContext().Request.QueryString.ToString();

            string requestBody = string.Empty;
            using (StreamReader reader = new(GetHttpContext().Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            switch (operationType)
            {
                case Operation.Find:
                    Context = new FindRequestContext(entityName, isList: string.IsNullOrEmpty(primaryKeyRoute));
                    break;
                case Operation.Insert:
                    JsonElement insertPayloadRoot = RequestValidator.ValidateInsertRequest(queryString, requestBody);
                    Context = new InsertRequestContext(entityName,
                        insertPayloadRoot,
                        HttpRestVerbs.POST,
                        operationType);
                    RequestValidator.ValidateInsertRequestContext(
                        (InsertRequestContext)Context,
                        MetadataStoreProvider);
                    break;
                case Operation.Delete:
                    Context = new DeleteRequestContext(entityName, isList: false);
                    RequestValidator.ValidateDeleteRequest(primaryKeyRoute);
                    break;
                case Operation.Upsert:
                case Operation.UpsertIncremental:
                    JsonElement upsertPayloadRoot = RequestValidator.ValidateUpsertRequest(primaryKeyRoute, requestBody);
                    Context = new UpsertRequestContext(entityName, upsertPayloadRoot, GetHttpVerb(operationType), operationType);
                    RequestValidator.ValidateUpsertRequestContext((UpsertRequestContext)Context, MetadataStoreProvider);
                    break;
                default:
                    throw new NotSupportedException("This operation is not yet supported.");
            }

            if (!string.IsNullOrEmpty(primaryKeyRoute))
            {
                // After parsing primary key, the Context will be populated with the
                // correct PrimaryKeyValuePairs.
                RequestParser.ParsePrimaryKey(primaryKeyRoute, Context);
                RequestValidator.ValidatePrimaryKey(Context, MetadataStoreProvider);
            }

            Context.NVC = HttpUtility.ParseQueryString(queryString);
            RequestParser.ParseQueryString(Context, MetadataStoreProvider.GetFilterParser());

            // At this point for DELETE, the primary key should be populated in the Request Context. 
            RequestValidator.ValidateRequestContext(Context, MetadataStoreProvider);

            // RestRequestContext is finalized for QueryBuilding and QueryExecution.
            // Perform Authorization check prior to moving forward in request pipeline.
            // RESTAuthorizationService
            AuthorizationResult authorizationResult = await _authorizationService.AuthorizeAsync(
                user: GetHttpContext().User,
                resource: Context,
                requirements: new[] { Context.HttpVerb });

            if (authorizationResult.Succeeded)
            {
                switch (operationType)
                {
                    case Operation.Find:
                        return await _queryEngine.ExecuteAsync(Context);
                    case Operation.Insert:
                    case Operation.Delete:
                    case Operation.Upsert:
                    case Operation.UpsertIncremental:
                        return await _mutationEngine.ExecuteAsync(Context);
                    default:
                        throw new NotSupportedException("This operation is not yet supported.");
                };
            }
            else
            {
                throw new DataGatewayException(
                    message: "Unauthorized",
                    statusCode: HttpStatusCode.Unauthorized,
                    subStatusCode: DataGatewayException.SubStatusCodes.AuthorizationCheckFailed
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
            TableDefinition tableDefinition = MetadataStoreProvider.GetTableDefinition(entityName);
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

        private static OperationAuthorizationRequirement GetHttpVerb(Operation operation)
        {
            switch (operation)
            {
                case Operation.Upsert:
                    return HttpRestVerbs.PUT;
                case Operation.UpsertIncremental:
                    return HttpRestVerbs.PATCH;
                case Operation.Delete:
                    return HttpRestVerbs.DELETE;
                case Operation.Insert:
                    return HttpRestVerbs.POST;
                case Operation.Find:
                    return HttpRestVerbs.GET;
                default:
                    throw new NotSupportedException("This operation is not yet supported.");
            }
        }
    }
}
