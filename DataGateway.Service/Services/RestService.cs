using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

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
        public SqlRuntimeConfigProvider RuntimeConfigProvider { get; }

        public RestService(
            IQueryEngine queryEngine,
            IMutationEngine mutationEngine,
            IRuntimeConfigProvider runtimeConfigProvider,
            IHttpContextAccessor httpContextAccessor,
            IAuthorizationService authorizationService
            )
        {
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _httpContextAccessor = httpContextAccessor;
            _authorizationService = authorizationService;

            if (runtimeConfigProvider is SqlRuntimeConfigProvider sqlRuntimeConfigProvider)
            {
                RuntimeConfigProvider = sqlRuntimeConfigProvider;
            }
            else
            {
                throw new ArgumentException(
                    $"${nameof(SqlRuntimeConfigProvider)} expected to be injected for ${nameof(IRuntimeConfigProvider)}.");
            }

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
            QueryString? query = GetHttpContext().Request.QueryString;
            string queryString = query is null ? string.Empty : GetHttpContext().Request.QueryString.ToString();

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
                        RuntimeConfigProvider);
                    break;
                case Operation.Delete:
                    context = new DeleteRequestContext(entityName, isList: false);
                    RequestValidator.ValidateDeleteRequest(primaryKeyRoute);
                    break;
                case Operation.Update:
                case Operation.UpdateIncremental:
                case Operation.Upsert:
                case Operation.UpsertIncremental:
                    JsonElement upsertPayloadRoot = RequestValidator.ValidateUpdateOrUpsertRequest(primaryKeyRoute, requestBody);
                    context = new UpsertRequestContext(entityName, upsertPayloadRoot, GetHttpVerb(operationType), operationType);
                    RequestValidator.ValidateUpsertRequestContext((UpsertRequestContext)context, RuntimeConfigProvider);
                    break;
                default:
                    throw new DataGatewayException(message: "This operation is not supported.",
                                                   statusCode: HttpStatusCode.BadRequest,
                                                   subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }

            if (!string.IsNullOrEmpty(primaryKeyRoute))
            {
                // After parsing primary key, the Context will be populated with the
                // correct PrimaryKeyValuePairs.
                RequestParser.ParsePrimaryKey(primaryKeyRoute, context);
                RequestValidator.ValidatePrimaryKey(context, RuntimeConfigProvider);
            }

            if (!string.IsNullOrWhiteSpace(queryString))
            {
                context.ParsedQueryString = HttpUtility.ParseQueryString(queryString);
                RequestParser.ParseQueryString(
                    context,
                    RuntimeConfigProvider.ODataFilterParser,
                    RuntimeConfigProvider.GetTableDefinition(context.EntityName).PrimaryKey);
            }

            // At this point for DELETE, the primary key should be populated in the Request Context.
            RequestValidator.ValidateRequestContext(context, RuntimeConfigProvider);

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
                        return FormatFindResult(await _queryEngine.ExecuteAsync(context), (FindRequestContext)context);
                    case Operation.Insert:
                    case Operation.Delete:
                    case Operation.Update:
                    case Operation.UpdateIncremental:
                    case Operation.Upsert:
                    case Operation.UpsertIncremental:
                        return await _mutationEngine.ExecuteAsync(context);
                    default:
                        throw new NotSupportedException("This operation is not yet supported.");
                };
            }
            else
            {
                throw new DataGatewayException(
                    message: "Forbidden",
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataGatewayException.SubStatusCodes.AuthorizationCheckFailed
                );
            }
        }

        /// <summary>
        /// Format the results from a Find operation. Check if there is a requirement
        /// for a nextLink, and if so, add this value to the array of JsonElements to
        /// be used later to format the response in the RestController.
        /// </summary>
        /// <param name="task">This task will return the resultant JsonDocument from the query.</param>
        /// <param name="context">The RequestContext.</param>
        /// <returns>A result from a Find operation that has been correctly formatted for the controller.</returns>
        private JsonDocument? FormatFindResult(JsonDocument? jsonDoc, FindRequestContext context)
        {
            if (jsonDoc is null)
            {
                return jsonDoc;
            }

            JsonElement jsonElement = jsonDoc.RootElement;

            // If the results are not a collection or if the query does not have a next page
            // no nextLink is needed, return JsonDocument as is
            if (jsonElement.ValueKind != JsonValueKind.Array || !SqlPaginationUtil.HasNext(jsonElement, context.First))
            {
                return jsonDoc;
            }

            // More records exist than requested, we know this by requesting 1 extra record,
            // that extra record is removed here.
            IEnumerable<JsonElement> rootEnumerated = jsonElement.EnumerateArray();
            JsonElement lastElement = rootEnumerated.Last();
            rootEnumerated = rootEnumerated.Take(rootEnumerated.Count() - 1);
            string after = SqlPaginationUtil.MakeCursorFromJsonElement(
                               element: rootEnumerated.Last(),
                               nextElement: lastElement,
                               orderByColumns: context.OrderByClauseInUrl,
                               primaryKey: RuntimeConfigProvider.GetTableDefinition(context.EntityName).PrimaryKey,
                               tableAlias: context.EntityName);

            // nextLink is the URL needed to get the next page of records using the same query options
            // with $after base64 encoded for opaqueness
            string path = UriHelper.GetEncodedUrl(GetHttpContext().Request).Split('?')[0];
            JsonElement nextLink = SqlPaginationUtil.CreateNextLink(
                                  path,
                                  nvc: context!.ParsedQueryString,
                                  after);
            rootEnumerated = rootEnumerated.Append(nextLink);
            return JsonDocument.Parse(JsonSerializer.Serialize(rootEnumerated));
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
            TableDefinition tableDefinition = RuntimeConfigProvider.GetTableDefinition(entityName);
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
                case Operation.Update:
                case Operation.Upsert:
                    return HttpRestVerbs.PUT;
                case Operation.UpdateIncremental:
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
