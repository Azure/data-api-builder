// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    public class CosmosMutationEngine : IMutationEngine
    {
        private const int PATCH_OPERATIONS_LIMIT = 10;

        private readonly CosmosClientProvider _clientProvider;
        private readonly IMetadataProviderFactory _metadataProviderFactory;
        private readonly IAuthorizationResolver _authorizationResolver;

        public CosmosMutationEngine(
            CosmosClientProvider clientProvider,
            IMetadataProviderFactory metadataProviderFactory,
            IAuthorizationResolver authorizationResolver)
        {
            _clientProvider = clientProvider;
            _metadataProviderFactory = metadataProviderFactory;
            _authorizationResolver = authorizationResolver;
        }

        private async Task<JObject> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object?> queryArgs, CosmosOperationMetadata resolver, string dataSourceName)
        {
            // TODO: add support for all mutation types
            // we only support CreateOrUpdate (Upsert) for now

            if (queryArgs == null)
            {
                // TODO: in which scenario the queryArgs is empty
                throw new ArgumentNullException(nameof(queryArgs));
            }

            CosmosClient? client = _clientProvider.Clients[dataSourceName];
            if (client is null)
            {
                throw new DataApiBuilderException(
                    message: "Cosmos DB has not been properly initialized",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed);
            }

            Container container = client.GetDatabase(resolver.DatabaseName)
                                        .GetContainer(resolver.ContainerName);

            ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
            // If authorization fails, an exception will be thrown and request execution halts.
            string graphQLType = context.Selection.Field.Type.NamedType().Name;
            string entityName = metadataProvider.GetEntityName(graphQLType);
            AuthorizeMutation(context, queryArgs, entityName, resolver.OperationType);

            JObject result;
            if (resolver.OperationType == EntityActionOperation.Patch)
            {
                result = await HandlePatchAsync(queryArgs, container);
            }
            else
            {
                ItemResponse<JObject>? response = resolver.OperationType switch
                {
                    EntityActionOperation.UpdateGraphQL => await HandleUpdateAsync(queryArgs, container),
                    EntityActionOperation.Create => await HandleCreateAsync(queryArgs, container),
                    EntityActionOperation.Delete => await HandleDeleteAsync(queryArgs, container),
                    _ => throw new NotSupportedException($"unsupported operation type: {resolver.OperationType}")
                };

                result = response.Resource;
            }

            string roleName = AuthorizationResolver.GetRoleOfGraphQLRequest(context);

            // The presence of READ permission is checked in the current role (with which the request is executed) as well as Anonymous role. This is because, for GraphQL requests,
            // READ permission is inherited by other roles from Anonymous role when present.
            bool isReadPermissionConfigured = _authorizationResolver.AreRoleAndOperationDefinedForEntity(entityName, roleName, EntityActionOperation.Read)
                                              || _authorizationResolver.AreRoleAndOperationDefinedForEntity(entityName, AuthorizationResolver.ROLE_ANONYMOUS, EntityActionOperation.Read);

            // Check read permission before returning the response to prevent unauthorized users from viewing the response.
            if (!isReadPermissionConfigured)
            {
                throw new DataApiBuilderException(message: $"The mutation operation {context.Selection.Field.Name} was successful but the current user is unauthorized to view the response due to lack of read permissions",
                                                  statusCode: HttpStatusCode.Forbidden,
                                                  subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
            }

            return result;
        }

        /// <inheritdoc/>
        public void AuthorizeMutation(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            string entityName,
            EntityActionOperation mutationOperation)
        {
            string clientRole = AuthorizationResolver.GetRoleOfGraphQLRequest(context);
            List<string> inputArgumentKeys;
            if (mutationOperation != EntityActionOperation.Delete)
            {
                inputArgumentKeys = BaseSqlQueryStructure.GetSubArgumentNamesFromGQLMutArguments(MutationBuilder.ITEM_INPUT_ARGUMENT_NAME, parameters);
            }
            else
            {
                inputArgumentKeys = parameters.Keys.ToList();
            }

            bool isAuthorized = mutationOperation switch
            {
                EntityActionOperation.UpdateGraphQL =>
                    _authorizationResolver.AreColumnsAllowedForOperation(entityName,
                        roleName: clientRole,
                        operation: EntityActionOperation.Update,
                        columns: inputArgumentKeys),
                EntityActionOperation.Patch =>
                    _authorizationResolver.AreColumnsAllowedForOperation(entityName,
                        roleName: clientRole,
                        operation: EntityActionOperation.Update,
                        columns: inputArgumentKeys),
                EntityActionOperation.Create =>
                    _authorizationResolver.AreColumnsAllowedForOperation(entityName,
                        roleName: clientRole,
                        operation: mutationOperation,
                        columns: inputArgumentKeys),
                EntityActionOperation.Delete => true,// Field level authorization is not supported for delete mutations. A requestor must be authorized
                                                     // to perform the delete operation on the entity to reach this point.
                _ => throw new DataApiBuilderException(
                                         message: "Invalid operation for GraphQL Mutation, must be Create, UpdateGraphQL, or Delete",
                                         statusCode: HttpStatusCode.BadRequest,
                                         subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest
                                         ),
            };
            if (!isAuthorized)
            {
                throw new DataApiBuilderException(
                    message: DataApiBuilderException.GRAPHQL_MUTATION_FIELD_AUTHZ_FAILURE,
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                );
            }
        }

        private static async Task<ItemResponse<JObject>> HandleDeleteAsync(IDictionary<string, object?> queryArgs, Container container)
        {
            string? partitionKey = null;
            string? id = null;

            if (queryArgs.TryGetValue(QueryBuilder.ID_FIELD_NAME, out object? idObj))
            {
                id = idObj?.ToString();
            }

            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException("id field is mandatory");
            }

            if (queryArgs.TryGetValue(QueryBuilder.PARTITION_KEY_FIELD_NAME, out object? partitionKeyObj))
            {
                partitionKey = partitionKeyObj?.ToString();
            }

            if (string.IsNullOrEmpty(partitionKey))
            {
                throw new InvalidDataException("Partition Key field is mandatory");
            }

            return await container.DeleteItemAsync<JObject>(id, new PartitionKey(partitionKey));
        }

        private static async Task<ItemResponse<JObject>> HandleCreateAsync(IDictionary<string, object?> queryArgs, Container container)
        {
            object? item = queryArgs[MutationBuilder.ITEM_INPUT_ARGUMENT_NAME];

            JObject? input;
            // Variables were provided to the mutation
            if (item is Dictionary<string, object?>)
            {
                input = (JObject?)ParseVariableInputItem(item);
            }
            else
            {
                // An inline argument was set
                input = (JObject?)ParseInlineInputItem(item);
            }

            if (input is null)
            {
                throw new InvalidDataException("Input Item field is invalid");
            }

            return await container.CreateItemAsync(input);
        }

        private static async Task<ItemResponse<JObject>> HandleUpdateAsync(IDictionary<string, object?> queryArgs, Container container)
        {
            string? partitionKey = null;
            string? id = null;

            if (queryArgs.TryGetValue(QueryBuilder.ID_FIELD_NAME, out object? idObj))
            {
                id = idObj?.ToString();
            }

            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException("id field is mandatory");
            }

            if (queryArgs.TryGetValue(QueryBuilder.PARTITION_KEY_FIELD_NAME, out object? partitionKeyObj))
            {
                partitionKey = partitionKeyObj?.ToString();
            }

            if (string.IsNullOrEmpty(partitionKey))
            {
                throw new InvalidDataException("Partition Key field is mandatory");
            }

            object? item = queryArgs[MutationBuilder.ITEM_INPUT_ARGUMENT_NAME];

            JObject? input;
            // Variables were provided to the mutation
            if (item is Dictionary<string, object?>)
            {
                input = (JObject?)ParseVariableInputItem(item);
            }
            else
            {
                // An inline argument was set
                input = (JObject?)ParseInlineInputItem(item);
            }

            if (input is null)
            {
                throw new InvalidDataException("Input Item field is invalid");
            }
            else
            {
                return await container.ReplaceItemAsync<JObject>(input, id, new PartitionKey(partitionKey), new ItemRequestOptions());
            }
        }

        /// <summary>
        /// Refer to https://learn.microsoft.com/azure/cosmos-db/partial-document-update for more details on patch operations
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        /// <exception cref="DataApiBuilderException"></exception>
        private static async Task<JObject> HandlePatchAsync(IDictionary<string, object?> queryArgs, Container container)
        {
            string? partitionKey = null;
            string? id = null;

            if (queryArgs.TryGetValue(QueryBuilder.ID_FIELD_NAME, out object? idObj))
            {
                id = idObj?.ToString();
            }

            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidDataException("id field is mandatory");
            }

            if (queryArgs.TryGetValue(QueryBuilder.PARTITION_KEY_FIELD_NAME, out object? partitionKeyObj))
            {
                partitionKey = partitionKeyObj?.ToString();
            }

            if (string.IsNullOrEmpty(partitionKey))
            {
                throw new InvalidDataException("Partition Key field is mandatory");
            }

            object? item = queryArgs[MutationBuilder.ITEM_INPUT_ARGUMENT_NAME];

            JObject? input;
            // Variables were provided to the mutation
            if (item is Dictionary<string, object?>)
            {
                input = (JObject?)ParseVariableInputItem(item);
            }
            else
            {
                // An inline argument was set
                input = (JObject?)ParseInlineInputItem(item);
            }

            if (input is null)
            {
                throw new InvalidDataException("Input Item field is invalid");
            }

            // This would contain the patch operations to be applied on the document
            List<PatchOperation> patchOperations = new();
            GeneratePatchOperations(input, "", patchOperations);

            if (patchOperations.Count <= 10)
            {
                return (await container.PatchItemAsync<JObject>(id, new PartitionKey(partitionKey), patchOperations)).Resource;
            }

            // maximum 10 patch operations can be applied in a single patch request,
            // Hence dividing into multiple patch request, if it is requested for more than 10 item at a time.
            TransactionalBatch batch = container.CreateTransactionalBatch(new PartitionKey(partitionKey));
            int numberOfBatches = -1;
            for (int counter = 0; counter < patchOperations.Count; counter += PATCH_OPERATIONS_LIMIT)
            {
                // Get next 10 patch operations from the list
                List<PatchOperation> chunk = patchOperations.GetRange(counter, Math.Min(10, patchOperations.Count - counter));
                batch = batch.PatchItem(id, chunk);
                numberOfBatches++;
            }

            TransactionalBatchResponse response = await batch.ExecuteAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new DataApiBuilderException(
                        message: "Failed to patch the item",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed);
            }

            return response.GetOperationResultAtIndex<JObject>(numberOfBatches).Resource;
        }

        /// <summary>
        /// This method generates the patch operations for the input JObject by traversing the JObject recursively.
        /// e.g. if the input JObject is { "a": { "b": "c" } },
        /// the generated patch operation would be "set /a/b c"
        /// </summary>
        /// <param name="jObject">JObject needs to be traversed</param>
        /// <param name="currentPath">Current Position of the json token</param>
        /// <param name="patchOperations">Generated Patch Operation</param>
        private static void GeneratePatchOperations(JObject jObject, string currentPath, List<PatchOperation> patchOperations)
        {
            foreach (JProperty property in jObject.Properties())
            {
                string newPath = currentPath + "/" + property.Name;

                if (property.Value.Type != JTokenType.Array && property.Value.Type == JTokenType.Object)
                {
                    // Skip generating JPaths for array-type properties
                    GeneratePatchOperations((JObject)property.Value, newPath, patchOperations);
                }
                else
                {
                    patchOperations.Add(PatchOperation.Set(newPath, property.Value));
                }
            }
        }

        /// <summary>
        /// The method is for parsing the mutation input object with nested inner objects when input is passed in as variables.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private static object? ParseVariableInputItem(object? item)
        {
            if (item is Dictionary<string, object?> inputItem)
            {
                JObject? createInput = new();

                foreach (string key in inputItem.Keys)
                {
                    if (inputItem.TryGetValue(key, out object? value) && value != null)
                    {
                        createInput.Add(new JProperty(key, JToken.FromObject(inputItem.GetValueOrDefault(key)!)));
                    }
                }

                return createInput;
            }

            return item;
        }

        /// <summary>
        /// The method is for parsing the mutation input object with nested inner objects when input is passing inline.
        /// </summary>
        /// <param name="item"> In the form of ObjectFieldNode, or List<ObjectFieldNode></param>
        /// <returns>In the form of JObject</returns>
        private static object? ParseInlineInputItem(object? item)
        {
            JObject? createInput = new();

            if (item is ObjectFieldNode node)
            {
                if (TypeHelper.IsPrimitiveType(node.Value.Kind))
                {
                    createInput
                        .Add(new JProperty(
                            node.Name.Value,
                            ParseInlineInputItem(TypeHelper.GetValue(node.Value))));
                }
                else
                {
                    createInput.Add(new JProperty(node.Name.Value, ParseInlineInputItem(node.Value.Value)));

                }

                return createInput;
            }

            if (item is List<ObjectFieldNode> nodeList)
            {
                foreach (ObjectFieldNode subfield in nodeList)
                {
                    if (TypeHelper.IsPrimitiveType(subfield.Value.Kind))
                    {
                        createInput
                            .Add(new JProperty(
                                subfield.Name.Value,
                                ParseInlineInputItem(TypeHelper.GetValue(subfield.Value))));
                    }
                    else
                    {
                        createInput.Add(new JProperty(subfield.Name.Value, ParseInlineInputItem(subfield.Value.Value)));
                    }
                }

                return createInput;
            }

            // For nested array objects
            if (item is List<IValueNode> nodeArray)
            {
                JArray jArrayObj = new();

                foreach (IValueNode subfield in nodeArray)
                {
                    if (TypeHelper.IsPrimitiveType(subfield.Kind))
                    {
                        jArrayObj.Add(ParseInlineInputItem(TypeHelper.GetValue(subfield)));
                    }
                    else
                    {
                        jArrayObj.Add(ParseInlineInputItem(subfield.Value));
                    }
                }

                return jArrayObj;
            }

            return item;
        }

        /// <summary>
        /// Executes the mutation query and return result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of graphql mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <param name="dataSourceName">dataSourceName to execute against.</param>
        /// <returns>JSON object result</returns>
        public async Task<Tuple<JsonDocument?, IMetadata?>> ExecuteAsync(IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            string dataSourceName)
        {
            ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
            string graphQLType = context.Selection.Field.Type.NamedType().Name;
            string entityName = metadataProvider.GetEntityName(graphQLType);
            string databaseName = metadataProvider.GetSchemaName(entityName);
            string containerName = metadataProvider.GetDatabaseObjectName(entityName);

            string graphqlMutationName = context.Selection.Field.Name;
            EntityActionOperation mutationOperation =
                MutationBuilder.DetermineMutationOperationTypeBasedOnInputType(graphqlMutationName);

            CosmosOperationMetadata mutation = new(databaseName, containerName, mutationOperation);
            // TODO: we are doing multiple round of serialization/deserialization
            // fixme
            JObject jObject = await ExecuteAsync(context, parameters, mutation, dataSourceName);
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            return new Tuple<JsonDocument?, IMetadata?>((jObject is null) ? null! : JsonDocument.Parse(jObject.ToString()), null);
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
        }

        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of REST mutation request.</param>
        /// <returns>JSON object result</returns>
        public Task<IActionResult?> ExecuteAsync(RestRequestContext context)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public Task<IActionResult?> ExecuteAsync(StoredProcedureRequestContext context, string dataSourceName)
        {
            throw new NotImplementedException();
        }
    }
}
