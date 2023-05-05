// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Text;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Services.OpenAPI
{
    /// <summary>
    /// Service which generates and provides the OpenAPI description document
    /// describing the DAB engine's entity REST endpoint paths.
    /// </summary>
    public class OpenApiDocumentor : IOpenApiDocumentor
    {
        private ISqlMetadataProvider _metadataProvider;
        private RuntimeConfig _runtimeConfig;
        private OpenApiResponses _defaultOpenApiResponses;
        private OpenApiDocument? _openApiDocument;

        private const string DOCUMENTOR_VERSION = "PREVIEW";
        private const string DOCUMENTOR_UI_TITLE = "Data API builder - REST Endpoint";
        private const string GETALL_DESCRIPTION = "Returns entities.";
        private const string GETONE_DESCRIPTION = "Returns an entity.";
        private const string POST_DESCRIPTION = "Create entity.";
        private const string PUT_DESCRIPTION = "Replace or create entity.";
        private const string PATCH_DESCRIPTION = "Update or create entity.";
        private const string DELETE_DESCRIPTION = "Delete entity.";
        private const string RESPONSE_VALUE_PROPERTY = "value";
        private const string RESPONSE_ARRAY_PROPERTY = "array";

        // Error messages
        public const string DOCUMENT_ALREADY_GENERATED_ERROR = "OpenAPI description document already generated.";
        public const string DOCUMENT_CREATION_UNSUPPORTED_ERROR = "OpenAPI description document can't be created when the REST endpoint is disabled globally.";

        /// <summary>
        /// Constructor denotes required services whose metadata is used to generate the OpenAPI description document.
        /// </summary>
        /// <param name="sqlMetadataProvider">Provides database object metadata.</param>
        /// <param name="runtimeConfigProvider">Provides entity/REST path metadata.</param>
        public OpenApiDocumentor(ISqlMetadataProvider sqlMetadataProvider, RuntimeConfigProvider runtimeConfigProvider)
        {
            _metadataProvider = sqlMetadataProvider;
            _runtimeConfig = runtimeConfigProvider.GetRuntimeConfiguration();
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

            if (!_runtimeConfig.RestGlobalSettings.Enabled)
            {
                throw new DataApiBuilderException(
                    message: DOCUMENT_CREATION_UNSUPPORTED_ERROR,
                    statusCode: HttpStatusCode.MethodNotAllowed,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.GlobalRestEndpointDisabled);
            }

            try
            {
                string restEndpointPath = _runtimeConfig.RestGlobalSettings.Path;
                OpenApiComponents components = new()
                {
                    Schemas = CreateComponentSchemas()
                };

                OpenApiDocument doc = new()
                {
                    Info = new OpenApiInfo
                    {
                        Version = DOCUMENTOR_VERSION,
                        Title = DOCUMENTOR_UI_TITLE,
                    },
                    Servers = new List<OpenApiServer>
                    {
                        new OpenApiServer { Url = $"{restEndpointPath}" }
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
                    subStatusCode: DataApiBuilderException.SubStatusCodes.OpenApiDocumentGenerationFailure,
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

            foreach (string entityName in _metadataProvider.EntityToDatabaseObject.Keys.ToList())
            {
                // Entities which disable their REST endpoint must not be included in
                // the OpenAPI description document.
                if (_runtimeConfig.Entities.TryGetValue(entityName, out Entity? entity) && entity is not null)
                {
                    if (entity.GetRestEnabledOrPathSettings() is bool restEnabled)
                    {
                        if (!restEnabled)
                        {
                            continue;
                        }
                    }
                }

                // Routes including primary key
                Tuple<string, OpenApiPathItem> path = BuildPath(entityName, includePrimaryKeyPathComponent: false);
                pathsCollection.Add(path.Item1, path.Item2);

                // Routes excluding primary key
                Tuple<string, OpenApiPathItem> pathGetAllPost = BuildPath(entityName, includePrimaryKeyPathComponent: true);
                pathsCollection.TryAdd(pathGetAllPost.Item1, pathGetAllPost.Item2);
            }

            return pathsCollection;
        }

        /// <summary>
        /// Includes Path with Operations(+responses) and Parameters
        /// Parameters are the placeholders for pk values in curly braces { } in the URL route
        /// localhost:5000/api/Entity/pk1/{pk1}/pk2/{pk2}
        /// more generically:
        /// /entityName OR /RestPathName followed by (/pk/{pkValue}) * Number of primary keys.
        /// </summary>
        /// <returns>Tuple of the path value: e.g. "/Entity/pk1/{pk1}"
        /// and path metadata.</returns>
        private Tuple<string, OpenApiPathItem> BuildPath(string entityName, bool includePrimaryKeyPathComponent)
        {
            SourceDefinition sourceDefinition = _metadataProvider.GetSourceDefinition(entityName);

            string entityRestPath = GetEntityRestPath(entityName);
            string entityBasePathComponent = $"/{entityRestPath}";

            // When the source's primary key(s) are autogenerated, the PUT, PATCH, and POST request
            // bodies must not include the primary key(s).
            string schemaReferenceId = DoesSourceContainsAutogeneratedPrimaryKey(sourceDefinition) ? $"{entityName}_NoPK" : $"{entityName}";

            bool requestBodyRequired = IsRequestBodyRequired(sourceDefinition);

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

            if (includePrimaryKeyPathComponent)
            {
                Tuple<string, List<OpenApiParameter>> pkComponents = CreatePrimaryKeyPathComponentAndParameters(entityName);
                string pkPathComponents = pkComponents.Item1;
                string fullPathComponent = entityBasePathComponent + pkPathComponents;

                OpenApiOperation getOperation = new()
                {
                    Description = GETONE_DESCRIPTION,
                    Tags = tags,
                    Responses = new(_defaultOpenApiResponses),
                };
                getOperation.Responses.Add(HttpStatusCode.OK.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: entityName));

                OpenApiOperation putOperation = new()
                {
                    Description = PUT_DESCRIPTION,
                    Tags = tags,
                    Responses = new(_defaultOpenApiResponses),
                    RequestBody = CreateOpenApiRequestBodyPayload(schemaReferenceId, requestBodyRequired)
                };
                putOperation.Responses.Add(HttpStatusCode.OK.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: entityName));
                putOperation.Responses.Add(HttpStatusCode.Created.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Created), responseObjectSchemaName: entityName));

                OpenApiOperation patchOperation = new()
                {
                    Description = PATCH_DESCRIPTION,
                    Tags = tags,
                    Responses = new(_defaultOpenApiResponses),
                    RequestBody = CreateOpenApiRequestBodyPayload(schemaReferenceId, requestBodyRequired)
                };
                patchOperation.Responses.Add(HttpStatusCode.OK.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: entityName));
                patchOperation.Responses.Add(HttpStatusCode.Created.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Created), responseObjectSchemaName: entityName));

                OpenApiOperation deleteOperation = new()
                {
                    Description = DELETE_DESCRIPTION,
                    Tags = tags,
                    Responses = new(_defaultOpenApiResponses),
                    RequestBody = CreateOpenApiRequestBodyPayload(schemaReferenceId, requestBodyRequired)
                };
                deleteOperation.Responses.Add(HttpStatusCode.NoContent.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.NoContent)));

                OpenApiPathItem openApiPathItem = new()
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>()
                    {
                        // Creation GET, POST, PUT, PATCH, DELETE operations
                        [OperationType.Get] = getOperation,
                        [OperationType.Put] = putOperation,
                        [OperationType.Patch] = patchOperation,
                        [OperationType.Delete] = deleteOperation
                    },
                    Parameters = pkComponents.Item2
                };

                return new(fullPathComponent, openApiPathItem);
            }
            else
            {
                OpenApiOperation getAllOperation = new()
                {
                    Description = GETALL_DESCRIPTION,
                    Tags = tags,
                    Responses = new(_defaultOpenApiResponses),
                };
                getAllOperation.Responses.Add(HttpStatusCode.OK.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.OK), responseObjectSchemaName: entityName));

                OpenApiOperation postOperation = new()
                {
                    Description = POST_DESCRIPTION,
                    Tags = tags,
                    Responses = new(_defaultOpenApiResponses),
                    RequestBody = CreateOpenApiRequestBodyPayload(schemaReferenceId, requestBodyRequired)
                };
                postOperation.Responses.Add(HttpStatusCode.Created.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Created), responseObjectSchemaName: entityName));
                postOperation.Responses.Add(HttpStatusCode.Conflict.ToString("D"), CreateOpenApiResponse(description: nameof(HttpStatusCode.Conflict)));

                OpenApiPathItem openApiPathItem = new()
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>()
                    {
                        [OperationType.Get] = getAllOperation,
                        [OperationType.Post] = postOperation
                    }
                };

                return new(entityBasePathComponent, openApiPathItem);
            }
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
        /// Parameters are the placeholders for pk values in curly braces { } in the URL route
        /// https://localhost:5000/api/Entity/pk1/{pk1}/pk2/{pk2}
        /// This function creates the string value "/Entity/pk1/{pk1}/pk2/{pk2}"
        /// and creates associated parameters.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <returns>Primary Key path component. Empty string if no primary keys exist on database object source definition.</returns>
        private Tuple<string, List<OpenApiParameter>> CreatePrimaryKeyPathComponentAndParameters(string entityName)
        {
            SourceDefinition sourceDefinition = _metadataProvider.GetSourceDefinition(entityName);
            List<OpenApiParameter> parameters = new();
            StringBuilder pkComponents = new();

            // Each primary key must be represented in the path component.
            foreach (string column in sourceDefinition.PrimaryKey)
            {
                string columnNameForComponent = column;

                if (_metadataProvider.TryGetExposedColumnName(entityName, column, out string? mappedColumnAlias) && !string.IsNullOrEmpty(mappedColumnAlias))
                {
                    columnNameForComponent = mappedColumnAlias;
                }

                // The SourceDefinition's Columns dictionary keys represent the original (unmapped) column names. 
                if (sourceDefinition.Columns.TryGetValue(column, out ColumnDefinition? columnDef))
                {
                    OpenApiSchema parameterSchema = new()
                    {
                        Type = columnDef is not null ? SystemTypeToJsonDataType(columnDef.SystemType) : string.Empty
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
        private static bool DoesSourceContainsAutogeneratedPrimaryKey(SourceDefinition sourceDefinition)
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
        /// A body is required when any one field
        /// - is auto generated
        /// - has a default value
        /// - is nullable
        /// </summary>
        /// <param name="sourceDef">Database object's source metadata.</param>
        /// <returns>True, when a body should be generated. Otherwise, false.</returns>
        private static bool IsRequestBodyRequired(SourceDefinition sourceDef)
        {
            foreach (KeyValuePair<string, ColumnDefinition> columnMetadata in sourceDef.Columns)
            {
                // The presence of a non-PK column which does not have any of the following properties
                // results in the body being required.
                if (columnMetadata.Value.HasDefault || columnMetadata.Value.IsNullable || columnMetadata.Value.IsAutoGenerated)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Resolves any REST path overrides present for the provided entity in the runtime config.
        /// If no overrides exist, returns the passed in entity name.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <returns>Returns the REST path name for the provided entity.</returns>
        private string GetEntityRestPath(string entityName)
        {
            string entityRestPath = entityName;
            object? entityRestSettings = _runtimeConfig.Entities[entityName].GetRestEnabledOrPathSettings();

            if (entityRestSettings is not null && entityRestSettings is string)
            {
                entityRestPath = (string)entityRestSettings;
                if (!string.IsNullOrEmpty(entityRestPath) && entityRestPath.StartsWith('/'))
                {
                    // Remove slash from start of rest path.
                    entityRestPath = entityRestPath.Substring(1);
                }
            }

            Assert.IsFalse(Equals('/', entityRestPath));
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
        private static OpenApiResponse CreateOpenApiResponse(string description, string? responseObjectSchemaName = null)
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
                        CreateResponseContainer(responseObjectSchemaName)
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
        private static OpenApiMediaType CreateResponseContainer(string responseObjectSchemaName)
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
                    RESPONSE_VALUE_PROPERTY,
                    responseRootSchema
                }
            };

            OpenApiMediaType responsePayload = new()
            {
                Schema = new()
                {
                    Properties = responseBodyProperties
                }
            };

            return responsePayload;
        }

        /// <summary>
        /// Builds the schema objects for all entities present in the runtime configuration.
        /// Two schemas per entity are created:
        /// 1) {EntityName}      -> Primary keys present in schema, used for request bodies (excluding GET) and all response bodies.
        /// 2) {EntityName}_NoPK -> No primary keys present in schema, used for POST requests where PK is autogenerated and GET (all).
        /// Schema objects can be referenced elsewhere in the OpenAPI document with the intent to reduce document verbosity.
        /// </summary>
        /// <returns>Collection of schemas for entities defined in the runtime configuration.</returns>
        private Dictionary<string, OpenApiSchema> CreateComponentSchemas()
        {
            Dictionary<string, OpenApiSchema> schemas = new();

            foreach (string entityName in _metadataProvider.EntityToDatabaseObject.Keys.ToList())
            {
                // Entities which disable their REST endpoint must not be included in
                // the OpenAPI description document.
                if (_runtimeConfig.Entities.TryGetValue(entityName, out Entity? entity) && entity is not null)
                {
                    if (entity.GetRestEnabledOrPathSettings() is bool restEnabled)
                    {
                        if (!restEnabled)
                        {
                            continue;
                        }
                    }
                }

                SourceDefinition sourceDefinition = _metadataProvider.GetSourceDefinition(entityName);
                List<string> exposedColumnNames = GetExposedColumnNames(entityName, sourceDefinition.Columns.Keys.ToList());

                // create component for FULL entity with PK.
                schemas.Add(entityName, CreateComponentSchema(entityName, fields: exposedColumnNames));

                // create component for entity with no PK
                // get list of columns - primary key columns then optimize
                foreach (string primaryKeyColumn in sourceDefinition.PrimaryKey)
                {
                    if (_metadataProvider.TryGetExposedColumnName(entityName, backingFieldName: primaryKeyColumn, out string? exposedColumnName)
                        && exposedColumnName is not null)
                    {
                        exposedColumnNames.Remove(exposedColumnName);
                    }
                }

                // create component for TABLE (view?) NOT STOREDPROC entity with no PK.
                DatabaseObject dbo = _metadataProvider.EntityToDatabaseObject[entityName];
                if (dbo.SourceType is not SourceType.StoredProcedure or SourceType.View)
                {
                    schemas.Add($"{entityName}_NoPK", CreateComponentSchema(entityName, fields: exposedColumnNames));
                }
            }

            return schemas;
        }

        /// <summary>
        /// Creates the schema object for an entity.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="fields">List of mapped (alias) field names.</param>
        /// <exception cref="DataApiBuilderException">Raised when an entity's database metadata can't be found.</exception>
        /// <returns>Entity's OpenApiSchema representation.</returns>
        private OpenApiSchema CreateComponentSchema(string entityName, List<string> fields)
        {
            if (!_metadataProvider.EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? dbObject) || dbObject is null)
            {
                throw new DataApiBuilderException(
                    message: "Entity's database object metadata not found.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.OpenApiDocumentAlreadyExists);
            }

            Dictionary<string, OpenApiSchema> properties = new();
            foreach (string field in fields)
            {
                if (_metadataProvider.TryGetBackingColumn(entityName, field, out string? backingColumnValue) && !string.IsNullOrEmpty(backingColumnValue))
                {
                    string typeMetadata = string.Empty;
                    string formatMetadata = string.Empty;
                    if (dbObject.SourceDefinition.Columns.TryGetValue(backingColumnValue, out ColumnDefinition? columnDef) && columnDef is not null)
                    {
                        typeMetadata = SystemTypeToJsonDataType(columnDef.SystemType).ToString().ToLower();
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
                Type = "object",
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
        private List<string> GetExposedColumnNames(string entityName, IEnumerable<string> unmappedColumnNames)
        {
            List<string> mappedColumnNames = new();

            foreach (string dbColumnName in unmappedColumnNames)
            {
                if (_metadataProvider.TryGetExposedColumnName(entityName, backingFieldName: dbColumnName, out string? exposedColumnName))
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

        /// <summary>
        /// Converts the CLR type to JsonDataType
        /// to meet the data type requirement set by the OpenAPI specification.
        /// The value returned is formatted for the OpenAPI spec "type" property.
        /// </summary>
        /// <param name="type">CLR type</param>
        /// <seealso cref="https://spec.openapis.org/oas/v3.0.1#data-types"/>
        /// <returns>Formatted JSON type name in lower case: e.g. number, string, boolean, etc.</returns>
        private static string SystemTypeToJsonDataType(Type type)
        {
            JsonDataType openApiTypeName = type.Name switch
            {
                "String" => JsonDataType.String,
                "Guid" => JsonDataType.String,
                "Byte" => JsonDataType.String,
                "Int16" => JsonDataType.Number,
                "Int32" => JsonDataType.Number,
                "Int64" => JsonDataType.Number,
                "Single" => JsonDataType.Number,
                "Double" => JsonDataType.Number,
                "Decimal" => JsonDataType.Number,
                "Float" => JsonDataType.Number,
                "Boolean" => JsonDataType.Boolean,
                "DateTime" => JsonDataType.String,
                "DateTimeOffset" => JsonDataType.String,
                "Byte[]" => JsonDataType.String,
                _ => JsonDataType.Undefined
            };

            string formattedOpenApiTypeName = openApiTypeName.ToString().ToLower();
            return formattedOpenApiTypeName;
        }
    }
}
