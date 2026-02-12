// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Mime;
using System.Text;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Core.Services.OpenAPI;
using Azure.DataApiBuilder.Product;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using static Azure.DataApiBuilder.Config.DabConfigEvents;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Service which generates and provides the OpenAPI description document
    /// describing the DAB engine's entity REST endpoint paths.
    /// </summary>
    public class OpenApiDocumentor : IOpenApiDocumentor
    {
        private readonly IMetadataProviderFactory _metadataProviderFactory;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly ILogger<OpenApiDocumentor> _logger;
        private OpenApiResponses _defaultOpenApiResponses;
        private OpenApiDocument? _openApiDocument;
        private readonly ConcurrentDictionary<string, string> _roleSpecificDocuments = new(StringComparer.OrdinalIgnoreCase);

        private const string DOCUMENTOR_UI_TITLE = "Data API builder - REST Endpoint";
        private const string GETALL_DESCRIPTION = "Returns entities.";
        private const string GETONE_DESCRIPTION = "Returns an entity.";
        private const string POST_DESCRIPTION = "Create entity.";
        private const string PUT_DESCRIPTION = "Replace or create entity.";
        private const string PATCH_DESCRIPTION = "Update or create entity.";
        private const string DELETE_DESCRIPTION = "Delete entity.";
        private const string SP_EXECUTE_DESCRIPTION = "Executes a stored procedure.";

        public const string SP_REQUEST_SUFFIX = "_sp_request";
        public const string SP_RESPONSE_SUFFIX = "_sp_response";
        public const string SCHEMA_OBJECT_TYPE = "object";
        public const string RESPONSE_ARRAY_PROPERTY = "array";

        // Routing constant
        public const string OPENAPI_ROUTE = "openapi";

        // OpenApi query parameters
        private static readonly List<OpenApiParameter> _tableAndViewQueryParameters = CreateTableAndViewQueryParameters();

        // Error messages
        public const string DOCUMENT_ALREADY_GENERATED_ERROR = "OpenAPI description document already generated.";
        public const string DOCUMENT_CREATION_UNSUPPORTED_ERROR = "OpenAPI description document can't be created when the REST endpoint is disabled globally.";
        public const string DOCUMENT_CREATION_FAILED_ERROR = "OpenAPI description document creation failed";

        /// <summary>
        /// Constructor denotes required services whose metadata is used to generate the OpenAPI description document.
        /// </summary>
        /// <param name="metadataProviderFactory">Provides database object metadata.</param>
        /// <param name="runtimeConfigProvider">Provides entity/REST path metadata.</param>
        /// <param name="handler">Hot reload event handler.</param>
        /// <param name="logger">Logger for diagnostic information.</param>
        public OpenApiDocumentor(
            IMetadataProviderFactory metadataProviderFactory,
            RuntimeConfigProvider runtimeConfigProvider,
            HotReloadEventHandler<HotReloadEventArgs>? handler,
            ILogger<OpenApiDocumentor> logger)
        {
            handler?.Subscribe(DOCUMENTOR_ON_CONFIG_CHANGED, OnConfigChanged);
            _metadataProviderFactory = metadataProviderFactory;
            _runtimeConfigProvider = runtimeConfigProvider;
            _logger = logger;
            _defaultOpenApiResponses = CreateDefaultOpenApiResponses();
        }

        public void OnConfigChanged(object? sender, HotReloadEventArgs args)
        {
            CreateDocument(doOverrideExistingDocument: true);
            _roleSpecificDocuments.Clear(); // Clear role-specific document cache on config change
        }

        /// <summary>
        /// Attempts to return an OpenAPI description document, if generated.
        /// </summary>
        /// <param name="document">String representation of JSON OpenAPI description document.</param>
        /// <returns>True (plus string representation of document), when document exists. False, otherwise.</returns>
        public bool TryGetDocument([NotNullWhen(true)] out string? document)
        {
            if (_openApiDocument is null)
            {
                document = null;
                return false;
            }

            using (StringWriter textWriter = new(CultureInfo.InvariantCulture))
            {
                OpenApiJsonWriter jsonWriter = new(textWriter);
                _openApiDocument.SerializeAsV3(jsonWriter);

                string jsonPayload = textWriter.ToString();
                document = jsonPayload;
                return true;
            }
        }

        /// <summary>
        /// Attempts to return a role-specific OpenAPI description document.
        /// </summary>
        /// <param name="role">The role name to filter permissions (case-insensitive).</param>
        /// <param name="document">String representation of JSON OpenAPI description document.</param>
        /// <returns>True if role exists and document generated. False if role not found or empty/whitespace.</returns>
        public bool TryGetDocumentForRole(string role, [NotNullWhen(true)] out string? document)
        {
            document = null;

            // Validate role is not null, empty, or whitespace
            if (string.IsNullOrWhiteSpace(role))
            {
                return false;
            }

            // Check cache first
            if (_roleSpecificDocuments.TryGetValue(role, out document))
            {
                return true;
            }

            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();

            // Check if the role exists in any entity's permissions using LINQ
            bool roleExists = runtimeConfig.Entities
                .Any(kvp => kvp.Value.Permissions?.Any(p => string.Equals(p.Role, role, StringComparison.OrdinalIgnoreCase)) == true);

            if (!roleExists)
            {
                return false;
            }

            try
            {
                OpenApiDocument? roleDoc = GenerateDocumentForRole(runtimeConfig, role);
                if (roleDoc is null)
                {
                    return false;
                }

                using StringWriter textWriter = new(CultureInfo.InvariantCulture);
                OpenApiJsonWriter jsonWriter = new(textWriter);
                roleDoc.SerializeAsV3(jsonWriter);
                document = textWriter.ToString();

                // Cache the role-specific document
                _roleSpecificDocuments.TryAdd(role, document);

                return true;
            }
            catch (Exception ex)
            {
                // Log exception details for debugging document generation failures
                _logger.LogError(ex, "Failed to generate OpenAPI document for role '{Role}'", role);
                return false;
            }
        }

        /// <summary>
        /// Generates an OpenAPI document filtered for a specific role.
        /// </summary>
        private OpenApiDocument? GenerateDocumentForRole(RuntimeConfig runtimeConfig, string role)
        {
            string restEndpointPath = runtimeConfig.RestPath;
            string? runtimeBaseRoute = runtimeConfig.Runtime?.BaseRoute;
            string url = string.IsNullOrEmpty(runtimeBaseRoute) ? restEndpointPath : runtimeBaseRoute + "/" + restEndpointPath;

            OpenApiComponents components = new()
            {
                Schemas = CreateComponentSchemas(runtimeConfig.Entities, runtimeConfig.DefaultDataSourceName, role, isRequestBodyStrict: runtimeConfig.IsRequestBodyStrict)
            };

            List<OpenApiTag> globalTags = new();
            foreach (KeyValuePair<string, Entity> kvp in runtimeConfig.Entities)
            {
                Entity entity = kvp.Value;
                if (!entity.Rest.Enabled || !HasAnyAvailableOperations(entity, role))
                {
                    continue;
                }

                string restPath = entity.Rest?.Path ?? kvp.Key;
                globalTags.Add(new OpenApiTag
                {
                    Name = restPath,
                    Description = string.IsNullOrWhiteSpace(entity.Description) ? null : entity.Description
                });
            }

            return new OpenApiDocument()
            {
                Info = new OpenApiInfo
                {
                    Version = ProductInfo.GetProductVersion(),
                    // Use the role name directly since it was already validated to exist in permissions
                    Title = $"{DOCUMENTOR_UI_TITLE} - {role}"
                },
                Servers = new List<OpenApiServer>
                {
                    new() { Url = url }
                },
                Paths = BuildPaths(runtimeConfig.Entities, runtimeConfig.DefaultDataSourceName, role),
                Components = components,
                Tags = globalTags
            };
        }

        /// <summary>
        /// Creates an OpenAPI description document using OpenAPI.NET.
        /// Document compliant with patches of OpenAPI V3.0 spec 3.0.0 and 3.0.1,
        /// aligned with specification support provided by Microsoft.OpenApi.
        /// </summary>
        /// <exception cref="DataApiBuilderException">Raised when document is already generated
        /// or a failure occurs during generation.</exception>
        /// <seealso cref="https://github.com/microsoft/OpenAPI.NET/blob/1.6.3/src/Microsoft.OpenApi/OpenApiSpecVersion.cs"/>
        public void CreateDocument(bool doOverrideExistingDocument = false)
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            if (_openApiDocument is not null && !doOverrideExistingDocument)
            {
                throw new DataApiBuilderException(
                    message: DOCUMENT_ALREADY_GENERATED_ERROR,
                    statusCode: HttpStatusCode.Conflict,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.OpenApiDocumentAlreadyExists);
            }

            if (!runtimeConfig.IsRestEnabled)
            {
                throw new DataApiBuilderException(
                    message: DOCUMENT_CREATION_UNSUPPORTED_ERROR,
                    statusCode: HttpStatusCode.MethodNotAllowed,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.GlobalRestEndpointDisabled);
            }

            try
            {
                string restEndpointPath = runtimeConfig.RestPath;
                string? runtimeBaseRoute = runtimeConfig.Runtime?.BaseRoute;
                string url = string.IsNullOrEmpty(runtimeBaseRoute) ? restEndpointPath : runtimeBaseRoute + "/" + restEndpointPath;
                OpenApiComponents components = new()
                {
                    Schemas = CreateComponentSchemas(runtimeConfig.Entities, runtimeConfig.DefaultDataSourceName, role: null, isRequestBodyStrict: runtimeConfig.IsRequestBodyStrict)
                };

                // Collect all entity tags and their descriptions for the top-level tags array
                // Only include entities that have REST enabled and at least one available operation
                List<OpenApiTag> globalTags = new();
                foreach (KeyValuePair<string, Entity> kvp in runtimeConfig.Entities)
                {
                    Entity entity = kvp.Value;
                    if (!entity.Rest.Enabled || !HasAnyAvailableOperations(entity))
                    {
                        continue;
                    }

                    string restPath = entity.Rest?.Path ?? kvp.Key;
                    globalTags.Add(new OpenApiTag
                    {
                        Name = restPath,
                        Description = string.IsNullOrWhiteSpace(entity.Description) ? null : entity.Description
                    });
                }

                OpenApiDocument doc = new()
                {
                    Info = new OpenApiInfo
                    {
                        Version = ProductInfo.GetProductVersion(),
                        Title = DOCUMENTOR_UI_TITLE
                    },
                    Servers = new List<OpenApiServer>
                    {
                        new() { Url = url }
                    },
                    Paths = BuildPaths(runtimeConfig.Entities, runtimeConfig.DefaultDataSourceName),
                    Components = components,
                    Tags = globalTags
                };
                _openApiDocument = doc;
            }
            catch (Exception ex)
            {
                throw new DataApiBuilderException(
                    message: "OpenAPI description document generation failed.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.OpenApiDocumentCreationFailure,
                    innerException: ex);
            }
        }

        /// <summary>
        /// Iterates through the runtime configuration's entities and generates the path object
        /// representing the DAB engine's supported HTTP verbs and relevant route restrictions:
        /// Paths including primary key:
        /// - GET (by ID), PUT, PATCH, DELETE
        /// Paths excluding primary key:
        /// - GET (all), POST
        /// </summary>
        /// <example>
        /// A path with primary key where the parameter in curly braces {} represents the preceding primary key's value.
        /// "/EntityName/primaryKeyName/{primaryKeyValue}"
        /// A path with no primary key nor parameter representing the primary key value:
        /// "/EntityName"
        /// </example>
        /// <param name="role">Optional role to filter permissions. If null, returns superset of all roles.</param>
        /// <returns>All possible paths in the DAB engine's REST API endpoint.</returns>
        private OpenApiPaths BuildPaths(RuntimeEntities entities, string defaultDataSourceName, string? role = null)
        {
            OpenApiPaths pathsCollection = new();

            ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(defaultDataSourceName);
            foreach (KeyValuePair<string, DatabaseObject> entityDbMetadataMap in metadataProvider.EntityToDatabaseObject)
            {
                string entityName = entityDbMetadataMap.Key;
                if (!entities.ContainsKey(entityName))
                {
                    // This can happen for linking entities which are not present in runtime config.
                    continue;
                }

                string entityRestPath = GetEntityRestPath(entities[entityName].Rest, entityName);
                string entityBasePathComponent = $"/{entityRestPath}";

                DatabaseObject dbObject = entityDbMetadataMap.Value;
                SourceDefinition sourceDefinition = metadataProvider.GetSourceDefinition(entityName);

                // Entities which disable their REST endpoint must not be included in
                // the OpenAPI description document.
                if (entities.TryGetValue(entityName, out Entity? entity) && entity is not null)
                {
                    if (!entity.Rest.Enabled)
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }

                // Set the tag's Description property to the entity's semantic description if present.
                OpenApiTag openApiTag = new()
                {
                    Name = entityRestPath,
                    Description = string.IsNullOrWhiteSpace(entity.Description) ? null : entity.Description
                };

                // The OpenApiTag will categorize all paths created using the entity's name or overridden REST path value.
                // The tag categorization will instruct OpenAPI document visualization tooling to display all generated paths together.
                List<OpenApiTag> tags = new()
                {
                    openApiTag
                };

                Dictionary<OperationType, bool> configuredRestOperations = GetConfiguredRestOperations(entity, dbObject, role);

                // Skip entities with no available operations
                if (!configuredRestOperations.ContainsValue(true))
                {
                    continue;
                }

                if (dbObject.SourceType is EntitySourceType.StoredProcedure)
                {
                    Dictionary<OperationType, OpenApiOperation> operations = CreateStoredProcedureOperations(
                        entityName: entityName,
                        sourceDefinition: sourceDefinition,
                        configuredRestOperations: configuredRestOperations,
                        tags: tags);

                    if (operations.Count > 0)
                    {
                        OpenApiPathItem openApiPathItem = new()
                        {
                            Operations = operations
                        };

                        pathsCollection.TryAdd(entityBasePathComponent, openApiPathItem);
                    }
                }
                else
                {
                    // Create operations for SourceType.Table and SourceType.View
                    // Operations including primary key
                    Dictionary<OperationType, OpenApiOperation> pkOperations = CreateOperations(
                        entityName: entityName,
                        sourceDefinition: sourceDefinition,
                        includePrimaryKeyPathComponent: true,
                        configuredRestOperations: configuredRestOperations,
                        tags: tags);

                    if (pkOperations.Count > 0)
                    {
                        Tuple<string, List<OpenApiParameter>> pkComponents = CreatePrimaryKeyPathComponentAndParameters(entityName, metadataProvider);
                        string pkPathComponents = pkComponents.Item1;
                        string fullPathComponent = entityBasePathComponent + pkPathComponents;

                        OpenApiPathItem openApiPkPathItem = new()
                        {
                            Operations = pkOperations,
                            Parameters = pkComponents.Item2
                        };

                        pathsCollection.TryAdd(fullPathComponent, openApiPkPathItem);
                    }

                    // Operations excluding primary key
                    Dictionary<OperationType, OpenApiOperation> operations = CreateOperations(
                        entityName: entityName,
                        sourceDefinition: sourceDefinition,
                        includePrimaryKeyPathComponent: false,
                        configuredRestOperations: configuredRestOperations,
                        tags: tags);

                    if (operations.Count > 0)
                    {
                        OpenApiPathItem openApiPathItem = new()
                        {
                            Operations = operations
                        };

                        pathsCollection.TryAdd(entityBasePathComponent, openApiPathItem);
                    }
                }
            }

            return pathsCollection;
        }

        /// <summary>
        /// Creates OpenApiOperation definitions for entities with SourceType.Table/View
        /// </summary>
        /// <param name="entityName">Name of the entity</param>
        /// <param name="sourceDefinition">Database object information</param>
        /// <param name="includePrimaryKeyPathComponent">Whether to create operations which will be mapped to
        /// a path containing primary key parameters.
        /// TRUE: GET (one), PUT, PATCH, DELETE
        /// FALSE: GET (Many), POST</param>
        /// <param name="configuredRestOperations">Operations available based on permissions.</param>
        /// <param name="tags">Tags denoting how the operations should be categorized.
        /// Typically one tag value, the entity's REST path.</param>
        /// <returns>Collection of operation types and associated definitions.</returns>
        private Dictionary<OperationType, OpenApiOperation> CreateOperations(
            string entityName,
            SourceDefinition sourceDefinition,
            bool includePrimaryKeyPathComponent,
            Dictionary<OperationType, bool> configuredRestOperations,
            List<OpenApiTag> tags)
        {
            Dictionary<OperationType, OpenApiOperation> openApiPathItemOperations = new();

            if (includePrimaryKeyPathComponent)
            {
                if (configuredRestOperations[OperationType.Get])
                {
                    OpenApiOperation getOperation = CreateBaseOperation(description: GETONE_DESCRIPTION, tags: tags);
                    AddQueryParameters(getOperation.Parameters);
                    getOperation.Responses.Add(HttpStatusCode.OK.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: entityName));
                    openApiPathItemOperations.Add(OperationType.Get, getOperation);
                }

                // Only calculate requestBodyRequired if PUT or PATCH operations are configured
                if (configuredRestOperations[OperationType.Put] || configuredRestOperations[OperationType.Patch])
                {
                    bool requestBodyRequired = IsRequestBodyRequired(sourceDefinition, considerPrimaryKeys: false);

                    if (configuredRestOperations[OperationType.Put])
                    {
                        OpenApiOperation putOperation = CreateBaseOperation(description: PUT_DESCRIPTION, tags: tags);
                        putOperation.RequestBody = CreateOpenApiRequestBodyPayload($"{entityName}_NoPK", requestBodyRequired);
                        putOperation.Responses.Add(HttpStatusCode.OK.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: entityName));
                        putOperation.Responses.Add(HttpStatusCode.Created.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Created), responseObjectSchemaName: entityName));
                        openApiPathItemOperations.Add(OperationType.Put, putOperation);
                    }

                    if (configuredRestOperations[OperationType.Patch])
                    {
                        OpenApiOperation patchOperation = CreateBaseOperation(description: PATCH_DESCRIPTION, tags: tags);
                        patchOperation.RequestBody = CreateOpenApiRequestBodyPayload($"{entityName}_NoPK", requestBodyRequired);
                        patchOperation.Responses.Add(HttpStatusCode.OK.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: entityName));
                        patchOperation.Responses.Add(HttpStatusCode.Created.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Created), responseObjectSchemaName: entityName));
                        openApiPathItemOperations.Add(OperationType.Patch, patchOperation);
                    }
                }

                if (configuredRestOperations[OperationType.Delete])
                {
                    OpenApiOperation deleteOperation = CreateBaseOperation(description: DELETE_DESCRIPTION, tags: tags);
                    deleteOperation.Responses.Add(HttpStatusCode.NoContent.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.NoContent)));
                    openApiPathItemOperations.Add(OperationType.Delete, deleteOperation);
                }

                return openApiPathItemOperations;
            }
            else
            {
                if (configuredRestOperations[OperationType.Get])
                {
                    OpenApiOperation getAllOperation = CreateBaseOperation(description: GETALL_DESCRIPTION, tags: tags);
                    AddQueryParameters(getAllOperation.Parameters);
                    getAllOperation.Responses.Add(
                        HttpStatusCode.OK.ToString("D"),
                        CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: entityName, includeNextLink: true));
                    openApiPathItemOperations.Add(OperationType.Get, getAllOperation);
                }

                if (configuredRestOperations[OperationType.Post])
                {
                    string postBodySchemaReferenceId = DoesSourceContainAutogeneratedPrimaryKey(sourceDefinition) ? $"{entityName}_NoAutoPK" : $"{entityName}";
                    OpenApiOperation postOperation = CreateBaseOperation(description: POST_DESCRIPTION, tags: tags);
                    postOperation.RequestBody = CreateOpenApiRequestBodyPayload(postBodySchemaReferenceId, IsRequestBodyRequired(sourceDefinition, considerPrimaryKeys: true));
                    postOperation.Responses.Add(HttpStatusCode.Created.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Created), responseObjectSchemaName: entityName));
                    postOperation.Responses.Add(HttpStatusCode.Conflict.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Conflict)));
                    openApiPathItemOperations.Add(OperationType.Post, postOperation);
                }

                return openApiPathItemOperations;
            }
        }

        /// <summary>
        /// Helper method to add query parameters like $select, $first, $orderby etc. to get and getAll operations for tables/views.
        /// </summary>
        /// <param name="parameters">List of parameters for the operation.</param>
        private static void AddQueryParameters(IList<OpenApiParameter> parameters)
        {
            foreach (OpenApiParameter openApiParameter in _tableAndViewQueryParameters)
            {
                parameters.Add(openApiParameter);
            }
        }

        /// <summary>
        /// Creates OpenApiOperation definitions for entities with SourceType.StoredProcedure
        /// </summary>
        /// <param name="entityName">Entity name.</param>
        /// <param name="sourceDefinition">Database object information.</param>
        /// <param name="configuredRestOperations">Collection of which operations should be created for the stored procedure. </param>
        /// <param name="tags">Tags denoting how the operations should be categorized.
        /// Typically one tag value, the entity's REST path.</param>
        /// <returns>Collection of operation types and associated definitions.</returns>
        private Dictionary<OperationType, OpenApiOperation> CreateStoredProcedureOperations(
            string entityName,
            SourceDefinition sourceDefinition,
            Dictionary<OperationType, bool> configuredRestOperations,
            List<OpenApiTag> tags)
        {
            Dictionary<OperationType, OpenApiOperation> openApiPathItemOperations = new();
            string spRequestObjectSchemaName = entityName + SP_REQUEST_SUFFIX;
            string spResponseObjectSchemaName = entityName + SP_RESPONSE_SUFFIX;

            if (configuredRestOperations[OperationType.Get])
            {
                OpenApiOperation getOperation = CreateBaseOperation(description: SP_EXECUTE_DESCRIPTION, tags: tags);
                AddStoredProcedureInputParameters(getOperation, (StoredProcedureDefinition)sourceDefinition);
                getOperation.Responses.Add(
                    HttpStatusCode.OK.ToString("D"),
                    CreateOpenApiResponse(
                        description: nameof(HttpStatusCode.OK),
                        responseObjectSchemaName: spResponseObjectSchemaName,
                        includeNextLink: false));
                openApiPathItemOperations.Add(OperationType.Get, getOperation);
            }

            if (configuredRestOperations[OperationType.Post])
            {
                // POST requests for stored procedure entities must include primary key(s) in request body.
                OpenApiOperation postOperation = CreateBaseOperation(description: SP_EXECUTE_DESCRIPTION, tags: tags);
                postOperation.RequestBody = CreateOpenApiRequestBodyPayload(spRequestObjectSchemaName, IsRequestBodyRequired(sourceDefinition, considerPrimaryKeys: true, isStoredProcedure: true));
                postOperation.Responses.Add(HttpStatusCode.Created.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Created), responseObjectSchemaName: spResponseObjectSchemaName));
                postOperation.Responses.Add(HttpStatusCode.Conflict.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Conflict)));
                openApiPathItemOperations.Add(OperationType.Post, postOperation);
            }

            // PUT and PATCH requests have the same criteria for deciding whether a request body is required.
            bool requestBodyRequired = IsRequestBodyRequired(sourceDefinition, considerPrimaryKeys: false, isStoredProcedure: true);

            if (configuredRestOperations[OperationType.Put])
            {
                // PUT requests for stored procedure entities must include primary key(s) in request body.
                OpenApiOperation putOperation = CreateBaseOperation(description: SP_EXECUTE_DESCRIPTION, tags: tags);
                putOperation.RequestBody = CreateOpenApiRequestBodyPayload(spRequestObjectSchemaName, requestBodyRequired);
                putOperation.Responses.Add(HttpStatusCode.OK.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: spResponseObjectSchemaName));
                putOperation.Responses.Add(HttpStatusCode.Created.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Created), responseObjectSchemaName: spResponseObjectSchemaName));
                openApiPathItemOperations.Add(OperationType.Put, putOperation);
            }

            if (configuredRestOperations[OperationType.Patch])
            {
                // PATCH requests for stored procedure entities must include primary key(s) in request body
                OpenApiOperation patchOperation = CreateBaseOperation(description: SP_EXECUTE_DESCRIPTION, tags: tags);
                patchOperation.RequestBody = CreateOpenApiRequestBodyPayload(spRequestObjectSchemaName, requestBodyRequired);
                patchOperation.Responses.Add(HttpStatusCode.OK.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: spResponseObjectSchemaName));
                patchOperation.Responses.Add(HttpStatusCode.Created.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Created), responseObjectSchemaName: spResponseObjectSchemaName));
                openApiPathItemOperations.Add(OperationType.Patch, patchOperation);
            }

            if (configuredRestOperations[OperationType.Delete])
            {
                OpenApiOperation deleteOperation = CreateBaseOperation(description: SP_EXECUTE_DESCRIPTION, tags: tags);
                deleteOperation.Responses.Add(HttpStatusCode.NoContent.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.NoContent)));
                openApiPathItemOperations.Add(OperationType.Delete, deleteOperation);
            }

            return openApiPathItemOperations;
        }

        /// <summary>
        /// Creates an OpenApiOperation object pre-populated with common properties used
        /// across all operation types (GET (one/all), POST, PUT, PATCH, DELETE)
        /// </summary>
        /// <param name="description">Description of the operation.</param>
        /// <param name="tags">Tags defining how to categorize the operation in the OpenAPI document.</param>
        /// <returns>OpenApiOperation</returns>
        private OpenApiOperation CreateBaseOperation(string description, List<OpenApiTag> tags)
        {
            OpenApiOperation operation = new()
            {
                Description = description,
                Tags = tags,
                Responses = new(_defaultOpenApiResponses)
            };

            // Add custom headers for operation.
            AddCustomHeadersToOperation(operation);
            return operation;
        }

        /// <summary>
        /// Helper method to populate operation parameters with all the custom headers like X-MS-API-ROLE, Authorization etc. headers.
        /// </summary>
        /// <param name="operation">OpenApi operation.</param>
        private static void AddCustomHeadersToOperation(OpenApiOperation operation)
        {
            OpenApiSchema stringParamSchema = new()
            {
                Type = JsonDataType.String.ToString().ToLower()
            };

            // Add parameter for X-MS-API-ROLE header.
            OpenApiParameter paramForClientHeader = new()
            {
                Required = false,
                In = ParameterLocation.Header,
                Name = AuthorizationResolver.CLIENT_ROLE_HEADER,
                Schema = stringParamSchema
            };
            operation.Parameters.Add(paramForClientHeader);

            // Add parameter for Authorization header.
            OpenApiParameter paramForAuthHeader = new()
            {
                Required = false,
                In = ParameterLocation.Header,
                Name = "Authorization",
                Schema = stringParamSchema
            };
            operation.Parameters.Add(paramForAuthHeader);
        }

        /// <summary>
        /// This method adds the input parameters from the stored procedure definition to the OpenApi operation parameters.
        /// A input parameter will be marked REQUIRED if default value is not available.
        /// </summary>
        private static void AddStoredProcedureInputParameters(OpenApiOperation operation, StoredProcedureDefinition spDefinition)
        {
            foreach ((string paramKey, ParameterDefinition parameterDefinition) in spDefinition.Parameters)
            {
                operation.Parameters.Add(
                    GetOpenApiQueryParameter(
                        name: paramKey,
                        description: "Input parameter for stored procedure arguments",
                        required: false,
                        type: TypeHelper.GetJsonDataTypeFromSystemType(parameterDefinition.SystemType).ToString().ToLower()
                    )
                );
            }
        }

        /// <summary>
        /// Creates a list of OpenAPI parameters for querying tables and views.
        /// The query parameters include $select, $filter, $orderby, $first, and $after, which allow the user to specify which fields to return,
        /// filter the results based on a predicate expression, sort the results, and paginate the results.
        /// </summary>
        /// <returns>A list of OpenAPI parameters.</returns>
        private static List<OpenApiParameter> CreateTableAndViewQueryParameters()
        {
            List<OpenApiParameter> parameters = new()
            {
                // Add $select query parameter
                GetOpenApiQueryParameter(
                    name: RequestParser.FIELDS_URL,
                    description: "A comma separated list of fields to return in the response.",
                    required: false,
                    type: "string"
                ),

                // Add $filter query parameter
                GetOpenApiQueryParameter(
                    name: RequestParser.FILTER_URL,
                    description: "An OData expression (an expression that returns a boolean value) using the entity's fields to retrieve a subset of the results.",
                    required: false,
                    type: "string"
                ),

                // Add $orderby query parameter
                GetOpenApiQueryParameter(
                    name: RequestParser.SORT_URL,
                    description: "Uses a comma-separated list of expressions to sort response items. Add 'desc' for descending order, otherwise it's ascending by default.",
                    required: false,
                    type: "string"
                ),

                // Add $first query parameter
                GetOpenApiQueryParameter(
                    name: RequestParser.FIRST_URL,
                    description: "An integer value that specifies the number of items to return. Default is 100.",
                    required: false,
                    type: "integer"
                ),

                // Add $after query parameter
                GetOpenApiQueryParameter(
                    name: RequestParser.AFTER_URL,
                    description: "An opaque string that specifies the cursor position after which results should be returned.",
                    required: false,
                    type: "string"
                )
            };

            return parameters;
        }

        /// <summary>
        /// Creates a new OpenAPI query parameter with the specified name, description, required flag, and data type.
        /// </summary>
        /// <param name="name">The name of the query parameter.</param>
        /// <param name="description">The description of the query parameter.</param>
        /// <param name="required">A flag indicating whether the query parameter is required.</param>
        /// <param name="type">The data type of the query parameter.</param>
        /// <returns>A new OpenAPI query parameter.</returns>
        private static OpenApiParameter GetOpenApiQueryParameter(string name, string description, bool required, string type)
        {
            return new OpenApiParameter
            {
                Name = name,
                In = ParameterLocation.Query,
                Description = description,
                Required = required,
                Schema = new OpenApiSchema
                {
                    Type = type
                }
            };
        }

        /// <summary>
        /// Returns collection of OpenAPI OperationTypes and associated flag indicating whether they are enabled
        /// for the engine's REST endpoint.
        /// Acts as a helper for stored procedures where the runtime config can denote any combination of REST verbs
        /// to enable.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="dbObject">Database object metadata, indicating entity SourceType</param>
        /// <param name="role">Optional role to filter permissions. If null, returns superset of all roles.</param>
        /// <returns>Collection of OpenAPI OperationTypes and whether they should be created.</returns>
        private static Dictionary<OperationType, bool> GetConfiguredRestOperations(Entity entity, DatabaseObject dbObject, string? role = null)
        {
            Dictionary<OperationType, bool> configuredOperations = new()
            {
                [OperationType.Get] = false,
                [OperationType.Post] = false,
                [OperationType.Put] = false,
                [OperationType.Patch] = false,
                [OperationType.Delete] = false
            };

            if (dbObject.SourceType == EntitySourceType.StoredProcedure && entity is not null)
            {
                List<SupportedHttpVerb>? spRestMethods;
                if (entity.Rest.Methods is not null)
                {
                    spRestMethods = entity.Rest.Methods.ToList();
                }
                else
                {
                    spRestMethods = new List<SupportedHttpVerb> { SupportedHttpVerb.Post };
                }

                if (spRestMethods is null)
                {
                    return configuredOperations;
                }

                foreach (SupportedHttpVerb restMethod in spRestMethods)
                {
                    switch (restMethod)
                    {
                        case SupportedHttpVerb.Get:
                            configuredOperations[OperationType.Get] = true;
                            break;
                        case SupportedHttpVerb.Post:
                            configuredOperations[OperationType.Post] = true;
                            break;
                        case SupportedHttpVerb.Put:
                            configuredOperations[OperationType.Put] = true;
                            break;
                        case SupportedHttpVerb.Patch:
                            configuredOperations[OperationType.Patch] = true;
                            break;
                        case SupportedHttpVerb.Delete:
                            configuredOperations[OperationType.Delete] = true;
                            break;
                        default:
                            break;
                    }
                }
            }
            else
            {
                // For tables/views, determine available operations from permissions
                // If role is specified, filter to that role only; otherwise, get superset of all roles
                // Note: PUT/PATCH require BOTH Create AND Update permissions (upsert semantics)
                if (entity?.Permissions is not null)
                {
                    bool hasCreate = false;
                    bool hasUpdate = false;

                    foreach (EntityPermission permission in entity.Permissions)
                    {
                        // Skip permissions for other roles if a specific role is requested
                        if (role is not null && !string.Equals(permission.Role, role, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (permission.Actions is null)
                        {
                            continue;
                        }

                        foreach (EntityAction action in permission.Actions)
                        {
                            if (action.Action == EntityActionOperation.All)
                            {
                                configuredOperations[OperationType.Get] = true;
                                configuredOperations[OperationType.Post] = true;
                                configuredOperations[OperationType.Delete] = true;
                                hasCreate = true;
                                hasUpdate = true;
                            }
                            else
                            {
                                switch (action.Action)
                                {
                                    case EntityActionOperation.Read:
                                        configuredOperations[OperationType.Get] = true;
                                        break;
                                    case EntityActionOperation.Create:
                                        configuredOperations[OperationType.Post] = true;
                                        hasCreate = true;
                                        break;
                                    case EntityActionOperation.Update:
                                        hasUpdate = true;
                                        break;
                                    case EntityActionOperation.Delete:
                                        configuredOperations[OperationType.Delete] = true;
                                        break;
                                }
                            }
                        }
                    }

                    // PUT/PATCH require both Create and Update permissions (upsert semantics)
                    if (hasCreate && hasUpdate)
                    {
                        configuredOperations[OperationType.Put] = true;
                        configuredOperations[OperationType.Patch] = true;
                    }
                }
            }

            return configuredOperations;
        }

        /// <summary>
        /// Checks if an entity has any available REST operations based on its permissions.
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <param name="role">Optional role to filter permissions. If null, checks all roles.</param>
        /// <returns>True if the entity has any available operations.</returns>
        private static bool HasAnyAvailableOperations(Entity entity, string? role = null)
        {
            if (entity?.Permissions is null || entity.Permissions.Length == 0)
            {
                return false;
            }

            foreach (EntityPermission permission in entity.Permissions)
            {
                // Skip permissions for other roles if a specific role is requested
                if (role is not null && !string.Equals(permission.Role, role, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (permission.Actions?.Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Filters the exposed column names based on the superset of available fields across role permissions.
        /// A field is included if at least one role (or the specified role) has access to it.
        /// </summary>
        /// <param name="entity">The entity to check permissions for.</param>
        /// <param name="exposedColumnNames">All exposed column names from the database.</param>
        /// <param name="role">Optional role to filter permissions. If null, returns superset of all roles.</param>
        /// <returns>Filtered set of column names that are available based on permissions.</returns>
        private static HashSet<string> FilterFieldsByPermissions(Entity entity, HashSet<string> exposedColumnNames, string? role = null)
        {
            if (entity?.Permissions is null || entity.Permissions.Length == 0)
            {
                return exposedColumnNames;
            }

            HashSet<string> availableFields = new();

            foreach (EntityPermission permission in entity.Permissions)
            {
                // Skip permissions for other roles if a specific role is requested
                if (role is not null && !string.Equals(permission.Role, role, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // If actions is not defined for a matching role, all fields are available
                if (permission.Actions is null)
                {
                    return exposedColumnNames;
                }

                foreach (EntityAction action in permission.Actions)
                {
                    // If Fields is null, all fields are available for this action
                    if (action.Fields is null)
                    {
                        availableFields.UnionWith(exposedColumnNames);
                        continue;
                    }

                    // Determine included fields using ternary - either all fields or explicitly listed
                    HashSet<string> actionFields = (action.Fields.Include is null || action.Fields.Include.Contains("*"))
                        ? new HashSet<string>(exposedColumnNames)
                        : new HashSet<string>(action.Fields.Include.Where(f => exposedColumnNames.Contains(f)));

                    // Remove excluded fields
                    if (action.Fields.Exclude is not null && action.Fields.Exclude.Count > 0)
                    {
                        if (action.Fields.Exclude.Contains("*"))
                        {
                            // Exclude all - no fields available for this action
                            actionFields.Clear();
                        }
                        else
                        {
                            actionFields.ExceptWith(action.Fields.Exclude);
                        }
                    }

                    // Add to superset of available fields
                    availableFields.UnionWith(actionFields);
                }
            }

            return availableFields;
        }

        /// <summary>
        /// Creates the request body definition, which includes the expected media type (application/json)
        /// and reference to request body schema.
        /// </summary>
        /// <param name="schemaReferenceId">Request body schema object name: For POST requests on entity 
        /// where the primary key is autogenerated, do not allow PK in post body.</param>
        /// <param name="requestBodyRequired">True/False, conditioned on whether the table's columns are either
        /// autogenerated, nullable, or hasDefaultValue</param>
        /// <returns>Request payload.</returns>
        private static OpenApiRequestBody CreateOpenApiRequestBodyPayload(string schemaReferenceId, bool requestBodyRequired)
        {
            OpenApiRequestBody requestBody = new()
            {
                Content = new Dictionary<string, OpenApiMediaType>()
                {
                    {
                        MediaTypeNames.Application.Json,
                        new()
                        {
                            Schema = new OpenApiSchema()
                            {
                                Reference = new OpenApiReference()
                                {
                                    Type = ReferenceType.Schema,
                                    Id = schemaReferenceId
                                }
                            }
                        }
                    }
                },
                Required = requestBodyRequired
            };

            return requestBody;
        }

        /// <summary>
        /// This function creates the primary key path component string value "/Entity/pk1/{pk1}/pk2/{pk2}"
        /// and creates associated parameters which are the placeholders for pk values in curly braces { } in the URL route
        /// https://localhost:5000/api/Entity/pk1/{pk1}/pk2/{pk2}
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <returns>Primary Key path component and associated parameters. Empty string if no primary keys exist on database object source definition.</returns>
        private static Tuple<string, List<OpenApiParameter>> CreatePrimaryKeyPathComponentAndParameters(string entityName, ISqlMetadataProvider metadataProvider)
        {
            SourceDefinition sourceDefinition = metadataProvider.GetSourceDefinition(entityName);
            List<OpenApiParameter> parameters = new();
            StringBuilder pkComponents = new();

            // Each primary key must be represented in the path component.
            foreach (string column in sourceDefinition.PrimaryKey)
            {
                string columnNameForComponent = column;

                if (metadataProvider.TryGetExposedColumnName(entityName, column, out string? mappedColumnAlias) && !string.IsNullOrEmpty(mappedColumnAlias))
                {
                    columnNameForComponent = mappedColumnAlias;
                }

                // The SourceDefinition's Columns dictionary keys represent the original (unmapped) column names. 
                if (sourceDefinition.Columns.TryGetValue(column, out ColumnDefinition? columnDef))
                {
                    OpenApiSchema parameterSchema = new()
                    {
                        Type = columnDef is not null ? TypeHelper.GetJsonDataTypeFromSystemType(columnDef.SystemType).ToString().ToLower() : string.Empty
                    };

                    OpenApiParameter openApiParameter = new()
                    {
                        Required = true,
                        In = ParameterLocation.Path,
                        Name = $"{columnNameForComponent}",
                        Schema = parameterSchema
                    };

                    parameters.Add(openApiParameter);
                    string pkComponent = $"/{columnNameForComponent}/{{{columnNameForComponent}}}";
                    pkComponents.Append(pkComponent);
                }
            }

            return new(pkComponents.ToString(), parameters);
        }

        /// <summary>
        /// Determines whether the database object has an autogenerated primary key
        /// used to distinguish which requests can supply a value for the primary key.
        /// e.g. a POST request definition may not define request body field that includes
        /// a primary key which is autogenerated.
        /// </summary>
        /// <param name="sourceDefinition">Database object metadata.</param>
        /// <returns>True, when the primary key is autogenerated. Otherwise, false.</returns>
        private static bool DoesSourceContainAutogeneratedPrimaryKey(SourceDefinition sourceDefinition)
        {
            bool sourceObjectHasAutogeneratedPK = false;
            // Create primary key path component.
            foreach (string column in sourceDefinition.PrimaryKey)
            {
                string columnNameForComponent = column;

                if (sourceDefinition.Columns.TryGetValue(columnNameForComponent, out ColumnDefinition? columnDef) && columnDef is not null && columnDef.IsAutoGenerated)
                {
                    sourceObjectHasAutogeneratedPK = true;
                    break;
                }
            }

            return sourceObjectHasAutogeneratedPK;
        }

        /// <summary>
        /// Evaluates a database object's fields to determine whether a request body is required.
        /// A request body would typically be included with
        /// - POST: primary key(s) considered because they are required to be in the request body when used.
        /// - PUT/PATCH: primary key(s) not considered because they are required to be in the request URI when used.
        /// A request body is required when any one field
        /// - is not auto generated
        /// - does not have a default value
        /// - is not nullable
        /// because a value must be provided for that field.
        /// </summary>
        /// <param name="sourceDef">Database object's source metadata.</param>
        /// <param name="considerPrimaryKeys">Whether primary keys should be evaluated against the criteria
        /// to require a request body.</param>
        /// <param name="isStoredProcedure">Whether the SourceDefinition represents a stored procedure.</param>
        /// <returns>True, when a body should be generated. Otherwise, false.</returns>
        private static bool IsRequestBodyRequired(SourceDefinition sourceDef, bool considerPrimaryKeys, bool isStoredProcedure = false)
        {
            bool requestBodyRequired = false;

            if (isStoredProcedure)
            {
                StoredProcedureDefinition spDef = (StoredProcedureDefinition)sourceDef;
                foreach (KeyValuePair<string, ParameterDefinition> parameterMetadata in spDef.Parameters)
                {
                    // A parameter which does not have any of the following properties
                    // results in the body being required so that a value can be provided.
                    if (!parameterMetadata.Value.HasConfigDefault)
                    {
                        requestBodyRequired = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (KeyValuePair<string, ColumnDefinition> columnMetadata in sourceDef.Columns)
                {
                    // Whether to consider primary keys when deciding if a body is required
                    // because some request bodies may not include primary keys(PUT, PATCH)
                    // while the (POST) request body does include primary keys (when not autogenerated).
                    if (sourceDef.PrimaryKey.Contains(columnMetadata.Key) && !considerPrimaryKeys)
                    {
                        continue;
                    }

                    // A column which does not have any of the following properties
                    // results in the body being required so that a value can be provided.
                    if (!columnMetadata.Value.HasDefault || !columnMetadata.Value.IsNullable || !columnMetadata.Value.IsAutoGenerated)
                    {
                        requestBodyRequired = true;
                        break;
                    }
                }
            }

            return requestBodyRequired;
        }

        /// <summary>
        /// Attempts to resolve the REST path override set for an entity in the runtime config.
        /// If no override exists, this method returns the passed in entityRestPath.
        /// </summary>
        /// <param name="entityRestSettings">Rest setting for the entity.</param>
        /// <param name="entityRestPath">String representing the entityRestPath, which is the entity name if entityRestSettings are null or empty.</param>
        /// <returns>Returns the REST path name for the provided entity with no starting slash: {entityName} or {entityRestPath}.</returns>
        private static string GetEntityRestPath(EntityRestOptions entityRestSettings, string entityRestPath)
        {
            if (!string.IsNullOrEmpty(entityRestSettings.Path))
            {
                // Remove slash from start of REST path.
                entityRestPath = entityRestSettings.Path.TrimStart('/');
            }

            return entityRestPath;
        }

        /// <summary>
        /// Creates the base OpenApiResponse object common to all requests where
        /// responses are of type "application/json".
        /// </summary>
        /// <param name="description">HTTP Response Code Name: OK, Created, BadRequest, etc.</param>
        /// <param name="responseObjectSchemaName">Schema used to represent response records.
        /// Null when an example (such as error codes) adds redundant verbosity.</param>
        /// <returns>Base OpenApiResponse object</returns>
        private static OpenApiResponse CreateOpenApiResponse(string description, string? responseObjectSchemaName = null, bool includeNextLink = false)
        {
            OpenApiResponse response = new()
            {
                Description = description
            };

            // No entityname means no response object schema should be included.
            // the entityname references the schema of the response object.
            if (!string.IsNullOrEmpty(responseObjectSchemaName))
            {
                Dictionary<string, OpenApiMediaType> contentDictionary = new()
                {
                    {
                        MediaTypeNames.Application.Json,
                        CreateResponseContainer(responseObjectSchemaName, includeNextLink)
                    }
                };
                response.Content = contentDictionary;
            }

            return response;
        }

        /// <summary>
        /// Creates the OpenAPI description of the response payload, excluding the result records:
        /// {
        ///     "value": [
        ///         {
        ///             "resultProperty": resultPropertyValue
        ///         }
        ///     ]
        /// }
        /// </summary>
        /// <param name="responseObjectSchemaName">Schema name of response payload.</param>
        /// <returns>The base response object container.</returns>
        private static OpenApiMediaType CreateResponseContainer(string responseObjectSchemaName, bool includeNextLink)
        {
            // schema for the response's collection of result records
            OpenApiSchema resultCollectionSchema = new()
            {
                Reference = new OpenApiReference()
                {
                    Type = ReferenceType.Schema,
                    Id = $"{responseObjectSchemaName}"
                }
            };

            // Schema for the response's root property "value"
            OpenApiSchema responseRootSchema = new()
            {
                Type = RESPONSE_ARRAY_PROPERTY,
                Items = resultCollectionSchema
            };

            Dictionary<string, OpenApiSchema> responseBodyProperties = new()
            {
                {
                    OpenApiConstants.Value,
                    responseRootSchema
                }
            };

            if (includeNextLink)
            {
                OpenApiSchema nextLinkSchema = new()
                {
                    Type = "string"
                };
                responseBodyProperties.Add("nextLink", nextLinkSchema);
            }

            OpenApiMediaType responsePayload = new()
            {
                Schema = new()
                {
                    Type = SCHEMA_OBJECT_TYPE,
                    Properties = responseBodyProperties
                }
            };

            return responsePayload;
        }

        /// <summary>
        /// Builds the schema objects for all entities present in the runtime configuration.
        /// Two schemas per entity are created:
        /// 1) {EntityName}      -> Primary keys present in schema, used for request bodies (excluding GET) and all response bodies.
        /// 2) {EntityName}_NoAutoPK -> No auto-generated primary keys present in schema, used for POST requests where PK is not autogenerated and GET (all).
        /// 3) {EntityName}_NoPK -> No primary keys present in schema, used for POST requests where PK is autogenerated and GET (all).
        /// Schema objects can be referenced elsewhere in the OpenAPI document with the intent to reduce document verbosity.
        /// </summary>
        /// <param name="role">Optional role to filter permissions. If null, returns superset of all roles.</param>
        /// <param name="isRequestBodyStrict">When true, request body schemas disallow extra fields.</param>
        /// <returns>Collection of schemas for entities defined in the runtime configuration.</returns>
        private Dictionary<string, OpenApiSchema> CreateComponentSchemas(RuntimeEntities entities, string defaultDataSourceName, string? role = null, bool isRequestBodyStrict = true)
        {
            Dictionary<string, OpenApiSchema> schemas = new();
            // for rest scenario we need the default datasource name.
            ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(defaultDataSourceName);

            foreach (KeyValuePair<string, DatabaseObject> entityDbMetadataMap in metadataProvider.EntityToDatabaseObject)
            {
                // Entities which disable their REST endpoint must not be included in
                // the OpenAPI description document.
                string entityName = entityDbMetadataMap.Key;
                DatabaseObject dbObject = entityDbMetadataMap.Value;

                if (!entities.TryGetValue(entityName, out Entity? entity) || !entity.Rest.Enabled || !HasAnyAvailableOperations(entity, role))
                {
                    // Don't create component schemas for:
                    // 1. Linking entity: The entity will be null when we are dealing with a linking entity, which is not exposed in the config.
                    // 2. Entity for which REST endpoint is disabled.
                    // 3. Entity with no available operations based on permissions.
                    continue;
                }

                SourceDefinition sourceDefinition = metadataProvider.GetSourceDefinition(entityName);
                HashSet<string> exposedColumnNames = GetExposedColumnNames(entityName, sourceDefinition.Columns.Keys.ToList(), metadataProvider);

                // Filter fields based on the superset of permissions across all roles (or specific role)
                exposedColumnNames = FilterFieldsByPermissions(entity, exposedColumnNames, role);

                HashSet<string> nonAutoGeneratedPKColumnNames = new();

                if (dbObject.SourceType is EntitySourceType.StoredProcedure)
                {
                    // Request body schema whose properties map to stored procedure parameters
                    DatabaseStoredProcedure spObject = (DatabaseStoredProcedure)dbObject;
                    schemas.Add(entityName + SP_REQUEST_SUFFIX, CreateSpRequestComponentSchema(fields: spObject.StoredProcedureDefinition.Parameters, isRequestBodyStrict: isRequestBodyStrict));

                    // Response body schema whose properties map to the stored procedure's first result set columns
                    // as described by sys.dm_exec_describe_first_result_set. 
                    // Response schemas don't need additionalProperties restriction
                    schemas.Add(entityName + SP_RESPONSE_SUFFIX, CreateComponentSchema(entityName, fields: exposedColumnNames, metadataProvider, entities, isRequestBodySchema: false));
                }
                else
                {
                    // Create component schema for FULL entity with all primary key columns (included auto-generated)
                    // which will typically represent the response body of a request or a stored procedure's request body.
                    // Response schemas don't need additionalProperties restriction
                    schemas.Add(entityName, CreateComponentSchema(entityName, fields: exposedColumnNames, metadataProvider, entities, isRequestBodySchema: false));

                    // Create an entity's request body component schema excluding autogenerated primary keys.
                    // A POST request requires any non-autogenerated primary key references to be in the request body.
                    foreach (string primaryKeyColumn in sourceDefinition.PrimaryKey)
                    {
                        // Non-Autogenerated primary key(s) should appear in the request body.
                        if (!sourceDefinition.Columns[primaryKeyColumn].IsAutoGenerated)
                        {
                            nonAutoGeneratedPKColumnNames.Add(primaryKeyColumn);
                            continue;
                        }

                        if (metadataProvider.TryGetExposedColumnName(entityName, backingFieldName: primaryKeyColumn, out string? exposedColumnName)
                            && exposedColumnName is not null)
                        {
                            exposedColumnNames.Remove(exposedColumnName);
                        }
                    }

                    // Request body schema for POST - apply additionalProperties based on strict mode
                    schemas.Add($"{entityName}_NoAutoPK", CreateComponentSchema(entityName, fields: exposedColumnNames, metadataProvider, entities, isRequestBodySchema: true, isRequestBodyStrict: isRequestBodyStrict));

                    // Create an entity's request body component schema excluding all primary keys
                    // by removing the tracked non-autogenerated primary key column names and removing them from
                    // the exposedColumnNames collection.
                    // The schema component without primary keys is used for PUT and PATCH operation request bodies because
                    // those operations require all primary key references to be in the URI path, not the request body.
                    foreach (string primaryKeyColumn in nonAutoGeneratedPKColumnNames)
                    {
                        if (metadataProvider.TryGetExposedColumnName(entityName, backingFieldName: primaryKeyColumn, out string? exposedColumnName)
                            && exposedColumnName is not null)
                        {
                            exposedColumnNames.Remove(exposedColumnName);
                        }
                    }

                    // Request body schema for PUT/PATCH - apply additionalProperties based on strict mode
                    schemas.Add($"{entityName}_NoPK", CreateComponentSchema(entityName, fields: exposedColumnNames, metadataProvider, entities, isRequestBodySchema: true, isRequestBodyStrict: isRequestBodyStrict));
                }
            }

            return schemas;
        }

        /// <summary>
        /// Creates the schema object for the request body of a stored procedure entity
        /// by creating a collection of properties which represent the stored procedure's parameters.
        /// Additionally, the property typeMetadata is sourced by converting the stored procedure
        /// parameter's SystemType to JsonDataType.
        /// </summary>
        /// <param name="fields">Collection of stored procedure parameter metadata.</param>
        /// <param name="isRequestBodyStrict">When true, sets additionalProperties to false.</param>
        /// <returns>OpenApiSchema object representing a stored procedure's request body.</returns>
        private static OpenApiSchema CreateSpRequestComponentSchema(Dictionary<string, ParameterDefinition> fields, bool isRequestBodyStrict = true)
        {
            Dictionary<string, OpenApiSchema> properties = new();
            HashSet<string> required = new();

            foreach (KeyValuePair<string, ParameterDefinition> kvp in fields)
            {
                string parameter = kvp.Key;
                ParameterDefinition def = kvp.Value;
                string typeMetadata = TypeHelper.GetJsonDataTypeFromSystemType(def.SystemType).ToString().ToLower();

                properties.Add(parameter, new OpenApiSchema()
                {
                    Type = typeMetadata,
                    Description = def.Description,
                    Default = def.Default is not null ? new OpenApiString(def.Default) : null
                });

                if (def.Required == true)
                {
                    required.Add(parameter);
                }
            }

            OpenApiSchema schema = new()
            {
                Type = SCHEMA_OBJECT_TYPE,
                Properties = properties,
                Required = required,
                // For request body schemas, set additionalProperties based on request-body-strict setting
                AdditionalPropertiesAllowed = !isRequestBodyStrict
            };

            return schema;
        }

        /// <summary>
        /// Creates the schema object for an entity by creating a collection of properties
        /// which represent the exposed (aliased) column names.
        /// Additionally, the property typeMetadata is sourced by converting the db column's
        /// SystemType to a JsonDataType.
        /// For stored procedure entities, columns are limited to those of the first result set
        /// which can be described by sys.dm_exec_describe_first_result_set.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="fields">List of mapped (alias) field names.</param>
        /// <param name="metadataProvider">Metadata provider for database objects.</param>
        /// <param name="entities">Runtime entities from configuration.</param>
        /// <param name="isRequestBodySchema">Whether this schema is for a request body (applies additionalProperties setting).</param>
        /// <param name="isRequestBodyStrict">When true and isRequestBodySchema, sets additionalProperties to false.</param>
        /// <exception cref="DataApiBuilderException">Raised when an entity's database metadata can't be found,
        /// indicating a failure due to the provided entityName.</exception>
        /// <returns>Entity's OpenApiSchema representation.</returns>
        private static OpenApiSchema CreateComponentSchema(
            string entityName,
            HashSet<string> fields,
            ISqlMetadataProvider metadataProvider,
            RuntimeEntities entities,
            bool isRequestBodySchema = false,
            bool isRequestBodyStrict = true)
        {
            if (!metadataProvider.EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? dbObject) || dbObject is null)
            {
                throw new DataApiBuilderException(
                    message: $"{DOCUMENT_CREATION_FAILED_ERROR}: Database object metadata not found for the entity {entityName}.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.OpenApiDocumentCreationFailure);
            }

            Dictionary<string, OpenApiSchema> properties = new();

            Entity? entityConfig = entities.TryGetValue(entityName, out Entity? ent) ? ent : null;

            // Get backing column metadata to resolve the correct system type which is then
            // used to resolve the correct Json data type. 
            foreach (string field in fields)
            {
                if (metadataProvider.TryGetBackingColumn(entityName, field, out string? backingColumnValue) && !string.IsNullOrEmpty(backingColumnValue))
                {
                    string typeMetadata = string.Empty;
                    string formatMetadata = string.Empty;
                    string? fieldDescription = null;

                    if (dbObject.SourceDefinition.Columns.TryGetValue(backingColumnValue, out ColumnDefinition? columnDef))
                    {
                        typeMetadata = TypeHelper.GetJsonDataTypeFromSystemType(columnDef.SystemType).ToString().ToLower();
                    }

                    if (entityConfig?.Fields != null)
                    {
                        FieldMetadata? fieldMetadata = entityConfig.Fields.FirstOrDefault(f => f.Alias == field || f.Name == field);
                        fieldDescription = fieldMetadata?.Description;
                    }

                    properties.Add(field, new OpenApiSchema()
                    {
                        Type = typeMetadata,
                        Format = formatMetadata,
                        Description = fieldDescription
                    });
                }
            }

            OpenApiSchema schema = new()
            {
                Type = SCHEMA_OBJECT_TYPE,
                Properties = properties,
                Description = entityConfig?.Description,
                // Response schemas always allow additional properties (true).
                // Request body schemas respect request-body-strict: strict=true → false, strict=false → true
                AdditionalPropertiesAllowed = !isRequestBodySchema || !isRequestBodyStrict
            };

            return schema;
        }

        /// <summary>
        /// Returns a list of mapped columns names given the input entity and list of unmapped (database) columns.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="unmappedColumnNames">List of unmapped column names for the entity.</param>
        /// <returns>List of mapped columns names</returns>
        private static HashSet<string> GetExposedColumnNames(string entityName, IEnumerable<string> unmappedColumnNames, ISqlMetadataProvider metadataProvider)
        {
            HashSet<string> mappedColumnNames = new();

            foreach (string dbColumnName in unmappedColumnNames)
            {
                if (metadataProvider.TryGetExposedColumnName(entityName, backingFieldName: dbColumnName, out string? exposedColumnName))
                {
                    if (exposedColumnName is not null)
                    {
                        mappedColumnNames.Add(exposedColumnName);
                    }
                }
            }

            return mappedColumnNames;
        }

        /// <summary>
        /// Creates the default collection of responses for all requests in the OpenAPI
        /// description document.
        /// The OpenApiResponses dictionary key represents the integer value of the HttpStatusCode,
        /// which is returned when using Enum.ToString("D").
        /// The "D" format specified "displays the enumeration entry as an integer value in the shortest representation possible."
        /// </summary>
        /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/standard/base-types/enumeration-format-strings#d-or-d"/>
        /// <returns>Collection of default responses (400, 401, 403, 404).</returns>
        private static OpenApiResponses CreateDefaultOpenApiResponses()
        {
            OpenApiResponses defaultResponses = new()
            {
                { HttpStatusCode.BadRequest.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.BadRequest)) },
                { HttpStatusCode.Unauthorized.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Unauthorized)) },
                { HttpStatusCode.Forbidden.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Forbidden)) },
                { HttpStatusCode.NotFound.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.NotFound)) }
            };

            return defaultResponses;
        }
    }
}
