// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Services.OpenAPI;
using Microsoft.OpenApi;
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
    [TestMethod]
    [DataRow("BooksTable", "books", EntitySourceType.Table, DisplayName = "Table with path parameter id")]
    [DataRow("BooksView", "books_view_all", EntitySourceType.View, DisplayName = "View with path parameter id")]
    public async Task TestPathParametersForTablesAndViews(string entityName, string objectName, EntitySourceType entitySourceType)
    {
        EntitySource entitySource = new(Object: objectName, entitySourceType, null, null);
        OpenApiDocument openApiDocument = await GenerateOpenApiDocumentForGivenEntityAsync(entityName, entitySource);

        Assert.AreEqual(2, openApiDocument.Paths.Count);
        Assert.IsTrue(openApiDocument.Paths.ContainsKey($"/{entityName}"));
        Assert.IsTrue(openApiDocument.Paths.ContainsKey($"/{entityName}/id/{{id}}"));
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
    /// Validates that the default set of query parameters are generated for GET methods in Table/Views.
    /// $select, $filter, $orderby, $first, $after are the query parameters.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="objectName">The name of the database object.</param>
    /// <param name="entitySourceType">The source type of the entity.</param>
    [TestMethod]
    [DataRow("BooksTable", "books", EntitySourceType.Table, DisplayName = "Table with query parameters")]
    [DataRow("BooksView", "books_view_all", EntitySourceType.View, DisplayName = "View with query parameters")]
    public async Task TestQueryParametersAddedForGEToperationOnTablesAndViews(string entityName, string objectName, EntitySourceType entitySourceType)
    {
        EntitySource entitySource = new(Object: objectName, entitySourceType, null, null);
        OpenApiDocument openApiDocument = await GenerateOpenApiDocumentForGivenEntityAsync(entityName, entitySource);

        Assert.AreEqual(2, openApiDocument.Paths.Count);
        Assert.IsTrue(openApiDocument.Paths.ContainsKey($"/{entityName}"));
        Assert.IsTrue(openApiDocument.Paths.ContainsKey($"/{entityName}/id/{{id}}"));

        // Asserting on all the parameters for Get All operation.
        Assert.IsTrue(openApiDocument.Paths[$"/{entityName}"].Operations.ContainsKey(OperationType.Get));
        OpenApiOperation openApiOperationForGetAll = openApiDocument.Paths[$"/{entityName}"].Operations[OperationType.Get];
        AssertOnAllDefaultQueryParameters(openApiOperationForGetAll.Parameters.ToList());

        // Assert that path with id has all the query parameters.
        Assert.IsTrue(openApiDocument.Paths[$"/{entityName}/id/{{id}}"].Operations.ContainsKey(OperationType.Get));
        OpenApiOperation openApiOperationForGetById = openApiDocument.Paths[$"/{entityName}/id/{{id}}"].Operations[OperationType.Get];
        AssertOnAllDefaultQueryParameters(openApiOperationForGetById.Parameters.ToList());
    }

    /// <summary>
    /// Validates that the default set of query parameters are generated for GET methods in Table/Views.
    /// $select, $filter, $orderby, $first, $after are the query parameters.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="objectName">The name of the database object.</param>
    /// <param name="entitySourceType">The source type of the entity.</param>
    [TestMethod]
    [DataRow("BooksTable", "books", EntitySourceType.Table, DisplayName = "Table with query parameters")]
    [DataRow("BooksView", "books_view_all", EntitySourceType.View, DisplayName = "View with query parameters")]
    public async Task TestQueryParametersExcludedFromNonReadOperationsOnTablesAndViews(string entityName, string objectName, EntitySourceType entitySourceType)
    {
        EntitySource entitySource = new(Object: objectName, entitySourceType, null, null);
        OpenApiDocument openApiDocument = await GenerateOpenApiDocumentForGivenEntityAsync(entityName, entitySource);

        Assert.AreEqual(2, openApiDocument.Paths.Count);
        Assert.IsTrue(openApiDocument.Paths.ContainsKey($"/{entityName}"));
        Assert.IsTrue(openApiDocument.Paths.ContainsKey($"/{entityName}/id/{{id}}"));

        // Assert that Query Parameters Excluded From NonReadOperations for path without id.
        OpenApiPathItem pathWithouId = openApiDocument.Paths[$"/{entityName}"];
        Assert.IsTrue(pathWithouId.Operations.ContainsKey(OperationType.Post));
        Assert.IsFalse(pathWithouId.Operations[OperationType.Post].Parameters.Any(param => param.In is ParameterLocation.Query));
        Assert.IsFalse(pathWithouId.Operations.ContainsKey(OperationType.Put));
        Assert.IsFalse(pathWithouId.Operations.ContainsKey(OperationType.Patch));
        Assert.IsFalse(pathWithouId.Operations.ContainsKey(OperationType.Delete));

        // Assert that Query Parameters Excluded From NonReadOperations for path with id.
        OpenApiPathItem pathWithId = openApiDocument.Paths[$"/{entityName}/id/{{id}}"];
        Assert.IsFalse(pathWithId.Operations.ContainsKey(OperationType.Post));
        Assert.IsTrue(pathWithId.Operations.ContainsKey(OperationType.Put));
        Assert.IsFalse(pathWithId.Operations[OperationType.Put].Parameters.Any(param => param.In is ParameterLocation.Query));
        Assert.IsTrue(pathWithId.Operations.ContainsKey(OperationType.Patch));
        Assert.IsFalse(pathWithId.Operations[OperationType.Patch].Parameters.Any(param => param.In is ParameterLocation.Query));
        Assert.IsTrue(pathWithId.Operations.ContainsKey(OperationType.Delete));
        Assert.IsFalse(pathWithId.Operations[OperationType.Delete].Parameters.Any(param => param.In is ParameterLocation.Query));
    }

    /// <summary>
    /// Validates that Input parameters are generated for Stored Procedures with GET operation.
    /// It also validates parameter metadata like type, name, location, required, etc.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="objectName">The name of the database object.</param>
    [TestMethod]
    public async Task TestInputParametersForStoredProcedures()
    {
        string entityName = "UpdateBookTitle";
        string objectName = "update_book_title";

        // Adding parameter metadata with a default value.
        List<ParameterMetadata> parameterMetadata = new()
        {
            new ParameterMetadata
            {
                Name = "id",
                Required = false
            },
            new ParameterMetadata
            {
                Name = "title",
                Required = false,
                Default = "Test Title"
            }
        };

        EntitySource entitySource = new(Object: objectName, Type: EntitySourceType.StoredProcedure, Parameters: parameterMetadata, KeyFields: null);
        OpenApiDocument openApiDocument = await GenerateOpenApiDocumentForGivenEntityAsync(
                                                entityName,
                                                entitySource,
                                                supportedHttpMethods: new SupportedHttpVerb[] { SupportedHttpVerb.Get });

        Assert.AreEqual(1, openApiDocument.Paths.Count);
        Assert.IsTrue(openApiDocument.Paths.ContainsKey($"/{entityName}"));
        OpenApiPathItem pathItem = openApiDocument.Paths.First().Value;
        Assert.AreEqual(1, pathItem.Operations.Count);
        Assert.IsTrue(pathItem.Operations.ContainsKey(OperationType.Get));

        OpenApiOperation operation = pathItem.Operations[OperationType.Get];
        Assert.AreEqual(2, operation.Parameters.Where(param => param.In is ParameterLocation.Query).Count());
        Assert.IsTrue(operation.Parameters.Any(param =>
            param.In is ParameterLocation.Query
            && param.Name.Equals("id")
            && param.Schema.Type.Equals("number")
            && param.Required is false));

        // Parameter with default value will be an optional query parameter.
        Assert.IsTrue(operation.Parameters.Any(param =>
            param.In is ParameterLocation.Query
            && param.Name.Equals("title")
            && param.Schema.Type.Equals("string")
            && param.Required is false));
    }

    /// <summary>
    /// Validates that input query parameters are not generated for Stored Procedures irrespective of whether the operation is a GET operation
    /// or any other supported http REST operation.
    /// </summary>
    [TestMethod]
    [DataRow("CountBooks", "count_books", new SupportedHttpVerb[] { SupportedHttpVerb.Get }, DisplayName = "StoredProcudure with no input parameters results in 0 created input query params.")]
    [DataRow("InsertBook", "insert_book", new SupportedHttpVerb[] { SupportedHttpVerb.Post }, DisplayName = "StoredProcedure without GET operations will results in 0 created input query params.")]
    public async Task TestStoredProcedureForNoQueryParameters(string entityName, string objectName, SupportedHttpVerb[] supportedHttpVerbs)
    {
        EntitySource entitySource = new(Object: objectName, EntitySourceType.StoredProcedure, Parameters: null, KeyFields: null);
        OpenApiDocument openApiDocument = await GenerateOpenApiDocumentForGivenEntityAsync(entityName, entitySource, supportedHttpVerbs);

        Assert.AreEqual(1, openApiDocument.Paths.Count);
        OpenApiPathItem pathItem = openApiDocument.Paths.First().Value;
        Assert.AreEqual(1, pathItem.Operations.Count);
        Assert.AreEqual(supportedHttpVerbs.First().ToString(), pathItem.Operations.Keys.First().ToString());
        Assert.IsFalse(pathItem.Operations.Values.First().Parameters.Any(param => param.In is ParameterLocation.Query));
    }

    /// <summary>
    /// validate that $select, $filter, $orderby, $first, $after query parameters are present in the openAPI document.
    /// </summary>
    private static void AssertOnAllDefaultQueryParameters(List<OpenApiParameter> openApiParameters)
    {
        int countOfDefaultQueryParams = openApiParameters.Where(openApiParameter => openApiParameter.In is ParameterLocation.Query).Count();
        Assert.AreEqual(5, countOfDefaultQueryParams);
        Assert.IsTrue(openApiParameters.Any(param => param.In is ParameterLocation.Query && param.Name.Equals("$select") && param.Schema.Type.Equals("string")));
        Assert.IsTrue(openApiParameters.Any(param => param.In is ParameterLocation.Query && param.Name.Equals("$filter") && param.Schema.Type.Equals("string")));
        Assert.IsTrue(openApiParameters.Any(param => param.In is ParameterLocation.Query && param.Name.Equals("$orderby") && param.Schema.Type.Equals("string")));
        Assert.IsTrue(openApiParameters.Any(param => param.In is ParameterLocation.Query && param.Name.Equals("$first") && param.Schema.Type.Equals("integer")));
        Assert.IsTrue(openApiParameters.Any(param => param.In is ParameterLocation.Query && param.Name.Equals("$after") && param.Schema.Type.Equals("string")));
    }

    /// <summary>
    /// Generate and return the OpenApiDocument for a single entity.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="entitySource">Database source for entity.</param>
    /// <param name="supportedMethods">Supported HTTP verbs for the entity.</param>
    private async static Task<OpenApiDocument> GenerateOpenApiDocumentForGivenEntityAsync(
        string entityName,
        EntitySource entitySource,
        SupportedHttpVerb[] supportedHttpMethods = null)
    {
        Entity entity = new(
            Source: entitySource,
            Fields: null,
            GraphQL: new(Singular: null, Plural: null, Enabled: false),
            Rest: new(Methods: supportedHttpMethods ?? EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
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

    /// <summary>
    /// Test to validate that the custom header parameters are present for each of the operation irrespective of the operation and the type
    /// of the entity.
    /// </summary>
    /// <param name="entityName">Name of the entity.</param>
    /// <param name="objectName">Name of the database object backing the entity.</param>
    /// <param name="entitySourceType">Sourcetype of the entity.</param>
    /// <returns></returns>
    [TestMethod]
    [DataRow("BooksTable", "books", EntitySourceType.Table, DisplayName = "Assert custom header presence in header parameters for table.")]
    [DataRow("BooksView", "books_view_all", EntitySourceType.View, DisplayName = "Assert custom header presence in header parameters for view.")]
    [DataRow("UpdateBookTitle", "update_book_title", EntitySourceType.StoredProcedure, DisplayName = "Assert custom header presence in header parameters for stored procedure.")]
    [DataRow("BooksTable", "books", EntitySourceType.Table, DisplayName = "Validate custom header presence in header parameters for table.")]
    [DataRow("BooksView", "books_view_all", EntitySourceType.View, DisplayName = "Validate custom header presence in header parameters for view.")]
    [DataRow("UpdateBookTitle", "update_book_title", EntitySourceType.StoredProcedure, DisplayName = "Validate custom header presence in header parameters for stored procedure.")]
    public async Task ValidateHeaderParametersForEntity(string entityName, string objectName, EntitySourceType entitySourceType)
    {
        EntitySource entitySource = new(Object: objectName, Type: entitySourceType, Parameters: null, KeyFields: null);
        OpenApiDocument openApiDocument = await GenerateOpenApiDocumentForGivenEntityAsync(entityName, entitySource);
        foreach (OpenApiPathItem pathItem in openApiDocument.Paths.Values)
        {
            foreach ((OperationType operationType, OpenApiOperation operation) in pathItem.Operations)
            {
                // Assert presence of Authorization header and the expected parameter properties for the header parameter.
                Assert.IsTrue(operation.Parameters.Any(
                    param => param.In is ParameterLocation.Header &&
                    AuthorizationResolver.AUTHORIZATION_HEADER.Equals(param.Name) &&
                    JsonDataType.String.ToString().ToLower().Equals(param.Schema.Type) &&
                    param.Required is false));

                // Assert presence of X-MS-API-ROLE header and the expected parameter properties for the header parameter.
                Assert.IsTrue(operation.Parameters.Any(
                    param => param.In is ParameterLocation.Header &&
                    AuthorizationResolver.CLIENT_ROLE_HEADER.Equals(param.Name) &&
                    JsonDataType.String.ToString().ToLower().Equals(param.Schema.Type) &&
                    param.Required is false));
            }
        }
    }
}
