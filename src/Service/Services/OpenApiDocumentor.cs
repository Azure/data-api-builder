// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Services
{
    public class OpenApiDocumentor : IOpenApiDocumentor
    {
        private ISqlMetadataProvider _metadataProvider;
        private IAuthorizationResolver _authorizationResolver;
        private OpenApiDocument? _openApiDocument;
        private RuntimeConfig _runtimeConfig;

        public OpenApiDocumentor(ISqlMetadataProvider sqlMetadataProvider, IAuthorizationResolver authorizationResolver, RuntimeConfigProvider runtimeConfigProvider)
        {
            _metadataProvider = sqlMetadataProvider;
            _authorizationResolver = authorizationResolver;
            _openApiDocument = null;
            _runtimeConfig = runtimeConfigProvider.GetRuntimeConfiguration();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
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

        public void CreateDocument()
        {
            if (_openApiDocument is not null)
            {
                throw new DataApiBuilderException(
                    message: "already created",
                    statusCode: System.Net.HttpStatusCode.Conflict,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.OpenApiDocumentAlreadyExists);
            }

            OpenApiDocument doc = new()
            {
                Info = new OpenApiInfo
                {
                    Version = "0.0.1",
                    Title = "Data API builder - OpenAPI Description Document",
                },
                Servers = new List<OpenApiServer>
                {
                    new OpenApiServer { Url = "https://localhost:5000/api/openapi" }
                },
                Paths = BuildPaths(),
                Components = BuildComponents()
            };
            _openApiDocument = doc;
        }

        public OpenApiPaths BuildPaths()
        {
            // iterate through entities
            // BuildPath on entity (which includes Generating Parameters)
            // Generate Operations (which include Generating Responses)
            OpenApiPaths pathsCollection = new();
            foreach (string entityName in _metadataProvider.EntityToDatabaseObject.Keys.ToList())
            {
                Tuple<string, OpenApiPathItem> path = BuildPath(entityName);
                pathsCollection.Add(path.Item1, path.Item2);
            }

            return pathsCollection;
        }

        /// <summary>
        /// Includes Path with Operations(+responses) and Parameters
        /// </summary>
        /// <returns></returns>
        public Tuple<string, OpenApiPathItem> BuildPath(string entityName)
        {
            // Create entity component
            object? entityRestSettings = _runtimeConfig.Entities[entityName].GetRestEnabledOrPathSettings();
            string entityPathValue = entityName;
            if (entityRestSettings is not null && entityRestSettings is string)
            {
                entityPathValue = (string)entityRestSettings;
                if (entityPathValue.StartsWith('/'))
                {
                    entityPathValue = entityPathValue.Substring(1);
                }

                Assert.IsFalse(string.IsNullOrEmpty(entityPathValue));
            }

            string entityComponent = $"/{entityPathValue}";
            Assert.IsFalse(entityComponent == "/");

            SourceDefinition sourceDefinition = _metadataProvider.GetSourceDefinition(entityName);
            StringBuilder pkComponents = new();
            List<OpenApiParameter> parameters = new();
            foreach (string column in sourceDefinition.PrimaryKey)
            {
                string columnNameForComponent = column;
                if (_metadataProvider.TryGetExposedColumnName(entityName, column, out string? mappedColumnAlias) && !string.IsNullOrEmpty(mappedColumnAlias))
                {
                    columnNameForComponent = mappedColumnAlias;
                }

                parameters.Add(new OpenApiParameter()
                {
                    Required = true,
                    In = ParameterLocation.Path,
                    Name = $"{columnNameForComponent}",
                    Schema = new OpenApiSchema()
                    {
                        Type = $"JSON DATA TYPE for {column}",
                        Format = $"OAS DATA TYPE for {column}"
                    }
                });
                string pkComponent = $"/{columnNameForComponent}/{{{columnNameForComponent}}}";
                pkComponents.Append(pkComponent);
            }
            Assert.IsFalse(string.IsNullOrEmpty(entityComponent));
            // /{entityName/RestPathName} + {/pk/{pkValue}} * N
            // CreateFullPathComponentAndParameters()
            return new Tuple<string, OpenApiPathItem>(
                entityComponent + pkComponents.ToString(),
                new OpenApiPathItem()
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Get] = new OpenApiOperation
                        {
                            Description = "Returns all pets from the system that the user has access to",
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse
                                {
                                    Description = "OK",
                                    Content = new Dictionary<string, OpenApiMediaType>()
                                        {
                                            {
                                                "application/json",
                                                new OpenApiMediaType()
                                                {
                                                    Schema = new OpenApiSchema()
                                                    {
                                                        Properties = new Dictionary<string, OpenApiSchema>()
                                                        {
                                                            {
                                                                "value",
                                                                new OpenApiSchema()
                                                                {
                                                                    Type = "array",
                                                                    Items = new OpenApiSchema()
                                                                    {
                                                                        Reference = new OpenApiReference()
                                                                        {
                                                                            Type = ReferenceType.Schema,
                                                                            Id = $"{entityName}"
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                }
                            }
                        }
                    },
                    Parameters = parameters
                }
            );
        }

        public OpenApiComponents BuildComponents()
        {
            return new OpenApiComponents()
            {
                Schemas = BuildComponentSchemas()
            };
        }

        public Dictionary<string, OpenApiSchema> BuildComponentSchemas()
        {
            Dictionary<string, OpenApiSchema> schemas = new();

            foreach (string entityName in _metadataProvider.EntityToDatabaseObject.Keys.ToList())
            {
                SourceDefinition sourceDefinition = _metadataProvider.GetSourceDefinition(entityName);
                List<string> columns = /*sourceDefinition is null ? new List<string>() : */sourceDefinition.Columns.Keys.ToList();

                // create component for FULL entity with PK.
                schemas.Add(entityName, CreateComponentSchema(entityName, fields: columns));

                // create component for entity with no PK
                // get list of columns - primary key columns then optimize

                foreach (string primaryKeyColumn in sourceDefinition.PrimaryKey)
                {
                    columns.Remove(primaryKeyColumn);
                }

                // create component for TABLE (view?) NOT STOREDPROC entity with no PK.
                DatabaseObject dbo =  _metadataProvider.EntityToDatabaseObject[entityName];
                if (dbo.SourceType is not SourceType.StoredProcedure or SourceType.View)
                {
                    schemas.Add($"{entityName}_NoPK", CreateComponentSchema(entityName, fields: columns));
                }
            }

            return schemas;
        }

        /// <summary>
        /// Input needs entity fields and field data type metadata
        /// Should this have conditional for creating component with PK field? or should that be handled before
        /// and only pass in a field list here to generalize?
        /// ex
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="fields">List of mapped (alias) field names.</param>
        /// <returns></returns>
        public OpenApiSchema CreateComponentSchema(string entityName, List<string> fields)
        {
            if (!_metadataProvider.EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? dbObject) || dbObject is null)
            {
                throw new DataApiBuilderException(message: "oops bad entity", statusCode: System.Net.HttpStatusCode.InternalServerError, subStatusCode: DataApiBuilderException.SubStatusCodes.OpenApiDocumentAlreadyExists);
            }

            Dictionary<string, OpenApiSchema> properties = new();

            foreach (string field in fields)
            {
                if(_metadataProvider.TryGetBackingColumn(entityName, field, out string? backingColumnValue) && !string.IsNullOrEmpty(backingColumnValue))
                {
                    properties.Add(field, new OpenApiSchema()
                    {
                        Type = $"JSON DATA TYPE for {backingColumnValue}",
                        Format = $"OAS DATA TYPE for {backingColumnValue}"
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

        public JsonValueKind SystemTypeToJsonValueKind(Type type)
        {
            return type.Name switch
            {
                "String" => JsonValueKind.String,
                "Guid" => JsonValueKind.String,
                "Byte" => JsonValueKind.String,
                "Int16" => JsonValueKind.Number,
                "Int32" => JsonValueKind.Number,
                "Int64" => JsonValueKind.Number,
                "Single" => JsonValueKind.Number,
                "Double" => JsonValueKind.Number,
                "Decimal" => JsonValueKind.Number,
                "Boolean" => JsonValueKind.True,
                "DateTime" => JsonValueKind.String,
                "DateTimeOffset" => JsonValueKind.String,
                "Byte[]" => JsonValueKind.String,
                _ => throw new DataApiBuilderException(
                        message: $"Column type {type} not handled by case. Please add a case resolving {type} to the appropriate GraphQL type",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.GraphQLMapping)
            };
        }

        /// <summary>
        /// Returns schema object representing an Entity's non-primary key fields. 
        /// </summary>
        /// <returns></returns>
        public OpenApiSchema EntityToSchemaObject()
        {
            return new OpenApiSchema();
        }

        /// <summary>
        /// Returns schema object representing Entity including primary key and non-primary key fields.
        /// </summary>
        /// <returns></returns>
        public OpenApiSchema FullEntityToSchemaObject()
        {
            return new OpenApiSchema();
        }

        /// <summary>
        /// Returns collection representing an entity's field metadata
        /// Key: field name
        /// Value: OpenApiSchema describing the (value) Type and (value) Format of the field.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, OpenApiSchema> BuildComponentProperties()
        {
            Dictionary<string, OpenApiSchema> fieldMetadata = new();
            return fieldMetadata;
        }
    }
}
