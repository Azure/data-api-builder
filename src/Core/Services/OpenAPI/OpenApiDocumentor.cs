// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Mime;
using System.Text;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Core.Services.OpenAPI;
using Azure.DataApiBuilder.Product;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Service which generates and provides the OpenAPI description document
    /// describing the DAB engine's entity REST endpoint paths.
    /// </summary>
    public class OpenApiDocumentor : IOpenApiDocumentor
    {
        private readonly IMetadataProviderFactory _metadataProviderFactory;
        private readonly RuntimeConfig _runtimeConfig;
        private OpenApiResponses _defaultOpenApiResponses;
        private OpenApiDocument? _openApiDocument;

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
        /// <param name="sqlMetadataProvider">Provides database object metadata.</param>
        /// <param name="runtimeConfigProvider">Provides entity/REST path metadata.</param>
        public OpenApiDocumentor(IMetadataProviderFactory metadataProviderFactory, RuntimeConfigProvider runtimeConfigProvider)
        {
            _metadataProviderFactory = metadataProviderFactory;
            _runtimeConfig = runtimeConfigProvider.GetConfig();
            _defaultOpenApiResponses = CreateDefaultOpenApiResponses();
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
        /// Creates an OpenAPI description document using OpenAPI.NET.
        /// Document compliant with patches of OpenAPI V3.0 spec 3.0.0 and 3.0.1,
        /// aligned with specification support provided by Microsoft.OpenApi.
        /// </summary>
        /// <exception cref="DataApiBuilderException">Raised when document is already generated
        /// or a failure occurs during generation.</exception>
        /// <seealso cref="https://github.com/microsoft/OpenAPI.NET/blob/1.6.3/src/Microsoft.OpenApi/OpenApiSpecVersion.cs"/>
        public void CreateDocument()
        {
            if (_openApiDocument is not null)
            {
                throw new DataApiBuilderException(
                    message: DOCUMENT_ALREADY_GENERATED_ERROR,
                    statusCode: HttpStatusCode.Conflict,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.OpenApiDocumentAlreadyExists);
            }

            if (!_runtimeConfig.IsRestEnabled)
            {
                throw new DataApiBuilderException(
                    message: DOCUMENT_CREATION_UNSUPPORTED_ERROR,
                    statusCode: HttpStatusCode.MethodNotAllowed,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.GlobalRestEndpointDisabled);
            }

            try
            {
                string restEndpointPath = _runtimeConfig.RestPath;
                string? runtimeBaseRoute = _runtimeConfig.Runtime?.BaseRoute;
                string url = string.IsNullOrEmpty(runtimeBaseRoute) ? restEndpointPath : runtimeBaseRoute + "/" + restEndpointPath;
                OpenApiComponents components = new()
                {
                    Schemas = CreateComponentSchemas()
                };

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
                    Paths = BuildPaths(),
                    Components = components
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
        /// <returns>All possible paths in the DAB engine's REST API endpoint.</returns>
        private OpenApiPaths BuildPaths()
        {
            OpenApiPaths pathsCollection = new();

            string defaultDataSourceName = _runtimeConfig.DefaultDataSourceName;
            ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(defaultDataSourceName);
            foreach (KeyValuePair<string, DatabaseObject> entityDbMetadataMap in metadataProvider.EntityToDatabaseObject)
            {
                string entityName = entityDbMetadataMap.Key;
                if (!_runtimeConfig.Entities.ContainsKey(entityName))
                {
                    // This can happen for linking entities which are not present in runtime config.
                    continue;
                }

                string entityRestPath = GetEntityRestPath(entityName);
                string entityBasePathComponent = $"/{entityRestPath}";

                DatabaseObject dbObject = entityDbMetadataMap.Value;
                SourceDefinition sourceDefinition = metadataProvider.GetSourceDefinition(entityName);

                // Entities which disable their REST endpoint must not be included in
                // the OpenAPI description document.
                if (_runtimeConfig.Entities.TryGetValue(entityName, out Entity? entity) && entity is not null)
                {
                    if (!entity.Rest.Enabled)
                    {
                        continue;
                    }
                }

                // Explicitly exclude setting the tag's Description property since the Name property is self-explanatory.
                OpenApiTag openApiTag = new()
                {
                    Name = entityRestPath
                };

                // The OpenApiTag will categorize all paths created using the entity's name or overridden REST path value.
                // The tag categorization will instruct OpenAPI document visualization tooling to display all generated paths together.
                List<OpenApiTag> tags = new()
                {
                    openApiTag
                };

                Dictionary<OperationType, bool> configuredRestOperations = GetConfiguredRestOperations(entityName, dbObject);

                if (dbObject.SourceType is EntitySourceType.StoredProcedure)
                {
                    Dictionary<OperationType, OpenApiOperation> operations = CreateStoredProcedureOperations(
                        entityName: entityName,
                        sourceDefinition: sourceDefinition,
                        configuredRestOperations: configuredRestOperations,
                        tags: tags);

                    OpenApiPathItem openApiPathItem = new()
                    {
                        Operations = operations
                    };

                    pathsCollection.TryAdd(entityBasePathComponent, openApiPathItem);
                }
                else
                {
                    // Create operations for SourceType.Table and SourceType.View
                    // Operations including primary key
                    Dictionary<OperationType, OpenApiOperation> pkOperations = CreateOperations(
                        entityName: entityName,
                        sourceDefinition: sourceDefinition,
                        includePrimaryKeyPathComponent: true,
                        tags: tags);

                    Tuple<string, List<OpenApiParameter>> pkComponents = CreatePrimaryKeyPathComponentAndParameters(entityName, metadataProvider);
                    string pkPathComponents = pkComponents.Item1;
                    string fullPathComponent = entityBasePathComponent + pkPathComponents;

                    OpenApiPathItem openApiPkPathItem = new()
                    {
                        Operations = pkOperations,
                        Parameters = pkComponents.Item2
                    };

                    pathsCollection.TryAdd(fullPathComponent, openApiPkPathItem);

                    // Operations excluding primary key
                    Dictionary<OperationType, OpenApiOperation> operations = CreateOperations(
                        entityName: entityName,
                        sourceDefinition: sourceDefinition,
                        includePrimaryKeyPathComponent: false,
                        tags: tags);

                    OpenApiPathItem openApiPathItem = new()
                    {
                        Operations = operations
                    };

                    pathsCollection.TryAdd(entityBasePathComponent, openApiPathItem);
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
        /// <param name="tags">Tags denoting how the operations should be categorized.
        /// Typically one tag value, the entity's REST path.</param>
        /// <returns>Collection of operation types and associated definitions.</returns>
        private Dictionary<OperationType, OpenApiOperation> CreateOperations(
            string entityName,
            SourceDefinition sourceDefinition,
            bool includePrimaryKeyPathComponent,
            List<OpenApiTag> tags)
        {
            Dictionary<OperationType, OpenApiOperation> openApiPathItemOperations = new();

            if (includePrimaryKeyPathComponent)
            {
                // The OpenApiResponses dictionary key represents the integer value of the HttpStatusCode,
                // which is returned when using Enum.ToString("D").
                // The "D" format specified "displays the enumeration entry as an integer value in the shortest representation possible."
                // It will only contain $select query parameter to allow the user to specify which fields to return.
                OpenApiOperation getOperation = CreateBaseOperation(description: GETONE_DESCRIPTION, tags: tags);
                AddQueryParameters(getOperation.Parameters);
                getOperation.Responses.Add(HttpStatusCode.OK.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: entityName));
                openApiPathItemOperations.Add(OperationType.Get, getOperation);

                // PUT and PATCH requests have the same criteria for decided whether a request body is required.
                bool requestBodyRequired = IsRequestBodyRequired(sourceDefinition, considerPrimaryKeys: false);

                // PUT requests must include the primary key(s) in the URI path and exclude from the request body,
                // independent of whether the PK(s) are autogenerated.
                OpenApiOperation putOperation = CreateBaseOperation(description: PUT_DESCRIPTION, tags: tags);
                putOperation.RequestBody = CreateOpenApiRequestBodyPayload($"{entityName}_NoPK", requestBodyRequired);
                putOperation.Responses.Add(HttpStatusCode.OK.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: entityName));
                putOperation.Responses.Add(HttpStatusCode.Created.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Created), responseObjectSchemaName: entityName));
                openApiPathItemOperations.Add(OperationType.Put, putOperation);

                // PATCH requests must include the primary key(s) in the URI path and exclude from the request body,
                // independent of whether the PK(s) are autogenerated.
                OpenApiOperation patchOperation = CreateBaseOperation(description: PATCH_DESCRIPTION, tags: tags);
                patchOperation.RequestBody = CreateOpenApiRequestBodyPayload($"{entityName}_NoPK", requestBodyRequired);
                patchOperation.Responses.Add(HttpStatusCode.OK.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: entityName));
                patchOperation.Responses.Add(HttpStatusCode.Created.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Created), responseObjectSchemaName: entityName));
                openApiPathItemOperations.Add(OperationType.Patch, patchOperation);

                OpenApiOperation deleteOperation = CreateBaseOperation(description: DELETE_DESCRIPTION, tags: tags);
                deleteOperation.Responses.Add(HttpStatusCode.NoContent.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.NoContent)));
                openApiPathItemOperations.Add(OperationType.Delete, deleteOperation);

                return openApiPathItemOperations;
            }
            else
            {
                // Primary key(s) are not included in the URI paths of the GET (all) and POST operations.
                OpenApiOperation getAllOperation = CreateBaseOperation(description: GETALL_DESCRIPTION, tags: tags);
                AddQueryParameters(getAllOperation.Parameters);
                getAllOperation.Responses.Add(
                    HttpStatusCode.OK.ToString("D"),
                    CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: entityName, includeNextLink: true));
                openApiPathItemOperations.Add(OperationType.Get, getAllOperation);

                // The POST body must include fields for primary key(s) which are not autogenerated because a value must be supplied
                // for those fields. {entityName}_NoAutoPK represents the schema component which has all fields except for autogenerated primary keys.
                // When no autogenerated primary keys exist, then all fields can be included in the POST body which is represented by the schema
                // component: {entityName}.
                string postBodySchemaReferenceId = DoesSourceContainAutogeneratedPrimaryKey(sourceDefinition) ? $"{entityName}_NoAutoPK" : $"{entityName}";

                OpenApiOperation postOperation = CreateBaseOperation(description: POST_DESCRIPTION, tags: tags);
                postOperation.RequestBody = CreateOpenApiRequestBodyPayload(postBodySchemaReferenceId, IsRequestBodyRequired(sourceDefinition, considerPrimaryKeys: true));
                postOperation.Responses.Add(HttpStatusCode.Created.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Created), responseObjectSchemaName: entityName));
                postOperation.Responses.Add(HttpStatusCode.Conflict.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Conflict)));
                openApiPathItemOperations.Add(OperationType.Post, postOperation);

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
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="dbObject">Database object metadata, indicating entity SourceType</param>
        /// <returns>Collection of OpenAPI OperationTypes and whether they should be created.</returns>
        private Dictionary<OperationType, bool> GetConfiguredRestOperations(string entityName, DatabaseObject dbObject)
        {
            Dictionary<OperationType, bool> configuredOperations = new()
            {
                [OperationType.Get] = false,
                [OperationType.Post] = false,
                [OperationType.Put] = false,
                [OperationType.Patch] = false,
                [OperationType.Delete] = false
            };

            if (dbObject.SourceType == EntitySourceType.StoredProcedure)
            {
                Entity entity = _runtimeConfig.Entities[entityName];

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
                configuredOperations[OperationType.Get] = true;
                configuredOperations[OperationType.Post] = true;
                configuredOperations[OperationType.Put] = true;
                configuredOperations[OperationType.Patch] = true;
                configuredOperations[OperationType.Delete] = true;
            }

            return configuredOperations;
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
        /// If no override exists, this method returns the passed in entity name.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <returns>Returns the REST path name for the provided entity with no starting slash: {entityName} or {entityRestPath}.</returns>
        private string GetEntityRestPath(string entityName)
        {
            string entityRestPath = entityName;
            EntityRestOptions entityRestSettings = _runtimeConfig.Entities[entityName].Rest;

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
        /// <returns>Collection of schemas for entities defined in the runtime configuration.</returns>
        private Dictionary<string, OpenApiSchema> CreateComponentSchemas()
        {
            Dictionary<string, OpenApiSchema> schemas = new();

            // for rest scenario we need the default datasource name.
            string defaultDataSourceName = _runtimeConfig.DefaultDataSourceName;
            ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(defaultDataSourceName);

            foreach (KeyValuePair<string, DatabaseObject> entityDbMetadataMap in metadataProvider.EntityToDatabaseObject)
            {
                // Entities which disable their REST endpoint must not be included in
                // the OpenAPI description document.
                string entityName = entityDbMetadataMap.Key;
                DatabaseObject dbObject = entityDbMetadataMap.Value;

                if (!_runtimeConfig.Entities.TryGetValue(entityName, out Entity? entity) || !entity.Rest.Enabled)
                {
                    // Don't create component schemas for:
                    // 1. Linking entity: The entity will be null when we are dealing with a linking entity, which is not exposed in the config.
                    // 2. Entity for which REST endpoint is disabled.
                    continue;
                }

                SourceDefinition sourceDefinition = metadataProvider.GetSourceDefinition(entityName);
                HashSet<string> exposedColumnNames = GetExposedColumnNames(entityName, sourceDefinition.Columns.Keys.ToList(), metadataProvider);
                HashSet<string> nonAutoGeneratedPKColumnNames = new();

                if (dbObject.SourceType is EntitySourceType.StoredProcedure)
                {
                    // Request body schema whose properties map to stored procedure parameters
                    DatabaseStoredProcedure spObject = (DatabaseStoredProcedure)dbObject;
                    schemas.Add(entityName + SP_REQUEST_SUFFIX, CreateSpRequestComponentSchema(fields: spObject.StoredProcedureDefinition.Parameters));

                    // Response body schema whose properties map to the stored procedure's first result set columns
                    // as described by sys.dm_exec_describe_first_result_set. 
                    schemas.Add(entityName + SP_RESPONSE_SUFFIX, CreateComponentSchema(entityName, fields: exposedColumnNames, metadataProvider));
                }
                else
                {
                    // Create component schema for FULL entity with all primary key columns (included auto-generated)
                    // which will typically represent the response body of a request or a stored procedure's request body.
                    schemas.Add(entityName, CreateComponentSchema(entityName, fields: exposedColumnNames, metadataProvider));

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

                    schemas.Add($"{entityName}_NoAutoPK", CreateComponentSchema(entityName, fields: exposedColumnNames, metadataProvider));

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

                    schemas.Add($"{entityName}_NoPK", CreateComponentSchema(entityName, fields: exposedColumnNames, metadataProvider));
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
        /// </summary>
        /// <param name="fields">Collection of stored procedure parameter metadata.</param>
        /// <returns>OpenApiSchema object representing a stored procedure's request body.</returns>
        private static OpenApiSchema CreateSpRequestComponentSchema(Dictionary<string, ParameterDefinition> fields)
        {
            Dictionary<string, OpenApiSchema> properties = new();

            foreach (string parameter in fields.Keys)
            {
                string typeMetadata = TypeHelper.GetJsonDataTypeFromSystemType(fields[parameter].SystemType).ToString().ToLower();

                properties.Add(parameter, new OpenApiSchema()
                {
                    Type = typeMetadata
                });
            }

            OpenApiSchema schema = new()
            {
                Type = SCHEMA_OBJECT_TYPE,
                Properties = properties
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
        /// <exception cref="DataApiBuilderException">Raised when an entity's database metadata can't be found,
        /// indicating a failure due to the provided entityName.</exception>
        /// <returns>Entity's OpenApiSchema representation.</returns>
        private static OpenApiSchema CreateComponentSchema(string entityName, HashSet<string> fields, ISqlMetadataProvider metadataProvider)
        {
            if (!metadataProvider.EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? dbObject) || dbObject is null)
            {
                throw new DataApiBuilderException(
                    message: $"{DOCUMENT_CREATION_FAILED_ERROR}: Database object metadata not found for the entity {entityName}.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.OpenApiDocumentCreationFailure);
            }

            Dictionary<string, OpenApiSchema> properties = new();

            // Get backing column metadata to resolve the correct system type which is then
            // used to resolve the correct Json data type. 
            foreach (string field in fields)
            {
                if (metadataProvider.TryGetBackingColumn(entityName, field, out string? backingColumnValue) && !string.IsNullOrEmpty(backingColumnValue))
                {
                    string typeMetadata = string.Empty;
                    string formatMetadata = string.Empty;
                    if (dbObject.SourceDefinition.Columns.TryGetValue(backingColumnValue, out ColumnDefinition? columnDef))
                    {
                        typeMetadata = TypeHelper.GetJsonDataTypeFromSystemType(columnDef.SystemType).ToString().ToLower();
                    }

                    properties.Add(field, new OpenApiSchema()
                    {
                        Type = typeMetadata,
                        Format = formatMetadata
                    });
                }
            }

            OpenApiSchema schema = new()
            {
                Type = SCHEMA_OBJECT_TYPE,
                Properties = properties
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
