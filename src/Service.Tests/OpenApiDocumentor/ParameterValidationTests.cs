// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OpenApiIntegration;

/// <summary>
/// Integration tests validating path and query parameters are created
/// for different entities in DAB's REST API endpoint.
/// Path parameters are defined in the path of a URL using curly braces {}.
/// For example, in the URL /books/{id}, id is a path parameter.
/// Query parameters are defined in the query string of a URL using the ? character
/// followed by a list of key-value pairs separated by & characters. 
/// For example, in the URL /books?author=John+Doe&page=2, author and page are query parameters.
/// </summary>
[TestCategory(TestCategory.MSSQL)]
[TestClass]
public class ParameterValidationTests
{
    private const string CUSTOM_CONFIG = "parameter-config.MsSql.json";
    private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;

    /// <summary>
    /// Validates the path parameters for GET methods.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="objectName">The name of the database object.</param>
    /// <param name="entitySourceType">The source type of the entity.</param>
    [DataTestMethod]
    [DataRow("BooksTable", "books", EntitySourceType.Table, DisplayName = "Table with path parameter id")]
    [DataRow("BooksView", "books_view_all", EntitySourceType.View, DisplayName = "View with path parameter id")]
    public async Task ValidatePathParametersForTablesAndViews(string entityName, string objectName, EntitySourceType entitySourceType)
    {
        EntitySource entitySource = new(Object: objectName, entitySourceType, null, null);
        OpenApiDocument openApiDocument = await GenerateOpenApiDocumentForGivenEntityAsync(entityName, entitySource);

        foreach ((string pathName, OpenApiPathItem pathItem) in openApiDocument.Paths)
        {
            string apiPathWithParam = $"/{entityName}/id/{{id}}";
            if (pathName.Equals(apiPathWithParam))
            {
                Assert.IsTrue(pathItem.Parameters.Count is 1);
                OpenApiParameter pathParameter = pathItem.Parameters.First();
                Assert.AreEqual("id", pathParameter.Name);
                Assert.AreEqual(ParameterLocation.Path, pathParameter.In);
                Assert.IsTrue(pathParameter.Required);
            }
            else
            {
                // Get All and POST method with path /entityName, will have no path parameters.
                Assert.IsTrue(pathItem.Parameters.Count is 0);
            }
        }
    }

    /// <summary>
    /// Validates that Query parameters are generated for GET methods in Table/Views.
    /// $select, $filter, $orderby, $first, $after are the query parameters.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="objectName">The name of the database object.</param>
    /// <param name="entitySourceType">The source type of the entity.</param>
    [DataTestMethod]
    [DataRow("BooksTable", "books", EntitySourceType.Table, DisplayName = "Table with query parameters")]
    [DataRow("BooksView", "books_view_all", EntitySourceType.View, DisplayName = "View with query parameters")]
    public async Task ValidateQueryParametersForTablesAndViews(string entityName, string objectName, EntitySourceType entitySourceType)
    {
        EntitySource entitySource = new(Object: objectName, entitySourceType, null, null);
        OpenApiDocument openApiDocument = await GenerateOpenApiDocumentForGivenEntityAsync(entityName, entitySource);

        foreach (OpenApiPathItem pathItem in openApiDocument.Paths.Values)
        {
            foreach ((OperationType operationType, OpenApiOperation operation) in pathItem.Operations)
            {
                if (operationType is OperationType.Get)
                {
                    Assert.IsTrue(operation.Parameters.Count is 5);

                    // Assert that it contains all the query parameters with appropriate type
                    Assert.IsTrue(operation.Parameters.Any(param => param.In is ParameterLocation.Query && param.Name is "$select" && param.Schema.Type is "string"));
                    Assert.IsTrue(operation.Parameters.Any(param => param.In is ParameterLocation.Query && param.Name is "$filter" && param.Schema.Type is "string"));
                    Assert.IsTrue(operation.Parameters.Any(param => param.In is ParameterLocation.Query && param.Name is "$orderby" && param.Schema.Type is "string"));
                    Assert.IsTrue(operation.Parameters.Any(param => param.In is ParameterLocation.Query && param.Name is "$first" && param.Schema.Type is "integer"));
                    Assert.IsTrue(operation.Parameters.Any(param => param.In is ParameterLocation.Query && param.Name is "$after" && param.Schema.Type is "string"));
                }
                else
                {
                    // No query parameters for other OperationTypes. 
                    Assert.IsFalse(operation.Parameters.Any(param => param.In is ParameterLocation.Query));
                }
            }
        }
    }

    /// <summary>
    /// Validates that Input parameters are generated for Stored Procedures with GET operation.
    /// Input parameters for stored procedures will be generated for GET operation.
    /// If a parameter default value is not provided in the config, it will marked as required.
    /// It also validates parameter metadatas like type, name, location, required, etc.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="objectName">The name of the database object.</param>
    [DataTestMethod]
    [DataRow("CountBooks", "count_books", DisplayName = "StoredProcedure with no parameters")]
    [DataRow("UpdateBookTitle", "update_book_title", DisplayName = "StoredProcedure with parameters")]
    public async Task ValidateInputParametersForStoredProcedures(string entityName, string objectName)
    {
        Dictionary<string, object> parameterDefaults = null;
        if (entityName is "UpdateBookTitle")
        {
            // Adding a parameter default value.
            parameterDefaults = new Dictionary<string, object> { { "title", "Test Title" } };
        }

        EntitySource entitySource = new(Object: objectName, EntitySourceType.StoredProcedure, parameterDefaults, null);
        OpenApiDocument openApiDocument = await GenerateOpenApiDocumentForGivenEntityAsync(entityName, entitySource);

        OpenApiPathItem pathItem = openApiDocument.Paths.First().Value;
        foreach ((OperationType operationType, OpenApiOperation operation) in pathItem.Operations)
        {
            if (entityName.Equals("UpdateBookTitle"))
            {
                if (operationType is OperationType.Get)
                {
                    Assert.IsTrue(operation.Parameters.Any(param =>
                    param.In is ParameterLocation.Query
                    && param.Name is "id"
                    && param.Schema.Type is "number"
                    && param.Required is true
                    ));

                    // Parameter with default value will be an optional query parameter.
                    Assert.IsTrue(operation.Parameters.Any(param =>
                        param.In is ParameterLocation.Query
                        && param.Name is "title"
                        && param.Schema.Type is "string"
                        && param.Required is false));
                }
                else
                {
                    // Input Parameters are supported only for GET requests
                    // For Other requests requestBody is used.
                    Assert.IsTrue(operation.Parameters.IsNullOrEmpty());
                }
            }
            else
            {
                // CountBookTitle doesn't require any query parameters.
                Assert.IsTrue(operation.Parameters.IsNullOrEmpty());
            }
        }
    }

    /// <summary>
    /// Generate and return the OpenApiDocument for a single entity.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="entitySource">Database source for entity.</param>
    private async static Task<OpenApiDocument> GenerateOpenApiDocumentForGivenEntityAsync(string entityName, EntitySource entitySource)
    {
        Entity entity = new(
            Source: entitySource,
            GraphQL: new(Singular: null, Plural: null, Enabled: false),
            Rest: new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
            Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
            Mappings: null,
            Relationships: null);

        Dictionary<string, Entity> entities = new()
        {
            { entityName, entity }
        };

        RuntimeEntities runtimeEntities = new(entities);
        return await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
            runtimeEntities: runtimeEntities,
            configFileName: CUSTOM_CONFIG,
            databaseEnvironment: MSSQL_ENVIRONMENT);
    }
}
