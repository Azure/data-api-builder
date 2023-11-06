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
    [DataTestMethod]
    [DataRow("BooksTable", "books", EntitySourceType.Table, DisplayName = "Table with query parameters")]
    [DataRow("BooksView", "books_view_all", EntitySourceType.View, DisplayName = "View with query parameters")]
    public async Task TestQueryParametersAddedForGEToperationOnTablesAndViews(string entityName, string objectName, EntitySourceType entitySourceType)
    {
        EntitySource entitySource = new(Object: objectName, entitySourceType, null, null);
        OpenApiDocument openApiDocument = await GenerateOpenApiDocumentForGivenEntityAsync(entityName, entitySource);

        Assert.AreEqual(2, openApiDocument.Paths.Count);
        Assert.IsTrue(openApiDocument.Paths.ContainsKey($"/{entityName}"));
        Assert.IsTrue(openApiDocument.Paths.ContainsKey($"/{entityName}/id/{{id}}"));

        // Assert that path without id has all the query parameters.
        Assert.IsTrue(openApiDocument.Paths[$"/{entityName}"].Operations.ContainsKey(OperationType.Get));
        Assert.AreEqual(5, openApiDocument.Paths[$"/{entityName}"].Operations[OperationType.Get].Parameters.Count);

        // Asserting on all the parameters
        List<OpenApiParameter> openApiParameters = openApiDocument.Paths[$"/{entityName}"].Operations[OperationType.Get].Parameters.ToList();
        Assert.IsTrue(openApiParameters.Any(param => param.In is ParameterLocation.Query && param.Name.Equals("$select") && param.Schema.Type.Equals("string")));
        Assert.IsTrue(openApiParameters.Any(param => param.In is ParameterLocation.Query && param.Name.Equals("$filter") && param.Schema.Type.Equals("string")));
        Assert.IsTrue(openApiParameters.Any(param => param.In is ParameterLocation.Query && param.Name.Equals("$orderby") && param.Schema.Type.Equals("string")));
        Assert.IsTrue(openApiParameters.Any(param => param.In is ParameterLocation.Query && param.Name.Equals("$first") && param.Schema.Type.Equals("integer")));
        Assert.IsTrue(openApiParameters.Any(param => param.In is ParameterLocation.Query && param.Name.Equals("$after") && param.Schema.Type.Equals("string")));

        // Assert that path with id has only one parameter.
        Assert.IsTrue(openApiDocument.Paths[$"/{entityName}/id/{{id}}"].Operations.ContainsKey(OperationType.Get));
        OpenApiOperation openApiOperation = openApiDocument.Paths[$"/{entityName}/id/{{id}}"].Operations[OperationType.Get];
        Assert.AreEqual(1, openApiOperation.Parameters.Count);

        //Assert that it is $select
        OpenApiParameter openApiParameter = openApiOperation.Parameters.First();
        Assert.AreEqual("$select", openApiParameter.Name);
        Assert.AreEqual(ParameterLocation.Query, openApiParameter.In);
        Assert.AreEqual("string", openApiParameter.Schema.Type);
    }

    /// <summary>
    /// Validates that the default set of query parameters are generated for GET methods in Table/Views.
    /// $select, $filter, $orderby, $first, $after are the query parameters.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="objectName">The name of the database object.</param>
    /// <param name="entitySourceType">The source type of the entity.</param>
    [DataTestMethod]
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
        Assert.IsTrue(pathWithouId.Operations[OperationType.Post].Parameters.IsNullOrEmpty());
        Assert.IsFalse(pathWithouId.Operations.ContainsKey(OperationType.Put));
        Assert.IsFalse(pathWithouId.Operations.ContainsKey(OperationType.Patch));
        Assert.IsFalse(pathWithouId.Operations.ContainsKey(OperationType.Delete));

        // Assert that Query Parameters Excluded From NonReadOperations for path with id.
        OpenApiPathItem pathWithId = openApiDocument.Paths[$"/{entityName}/id/{{id}}"];
        Assert.IsFalse(pathWithId.Operations.ContainsKey(OperationType.Post));
        Assert.IsTrue(pathWithId.Operations.ContainsKey(OperationType.Put));
        Assert.IsTrue(pathWithId.Operations[OperationType.Put].Parameters.IsNullOrEmpty());
        Assert.IsTrue(pathWithId.Operations.ContainsKey(OperationType.Patch));
        Assert.IsTrue(pathWithId.Operations[OperationType.Patch].Parameters.IsNullOrEmpty());
        Assert.IsTrue(pathWithId.Operations.ContainsKey(OperationType.Delete));
        Assert.IsTrue(pathWithId.Operations[OperationType.Delete].Parameters.IsNullOrEmpty());
    }

    /// <summary>
    /// Validates that Input parameters are generated for Stored Procedures with GET operation.
    /// If a parameter default value is not provided in the config, it will be marked as required.
    /// It also validates parameter metadata like type, name, location, required, etc.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="objectName">The name of the database object.</param>
    [TestMethod]
    public async Task TestInputParametersForStoredProcedures()
    {
        string entityName = "UpdateBookTitle";
        string objectName = "update_book_title";

        // Adding a parameter default value.
        Dictionary<string, object> parameterDefaults = new() { { "title", "Test Title" } };

        EntitySource entitySource = new(Object: objectName, EntitySourceType.StoredProcedure, parameterDefaults, null);
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
        Assert.AreEqual(2, operation.Parameters.Count);
        Assert.IsTrue(operation.Parameters.Any(param =>
            param.In is ParameterLocation.Query
            && param.Name.Equals("id")
            && param.Schema.Type.Equals("number")
            && param.Required is true));

        // Parameter with default value will be an optional query parameter.
        Assert.IsTrue(operation.Parameters.Any(param =>
            param.In is ParameterLocation.Query
            && param.Name.Equals("title")
            && param.Schema.Type.Equals("string")
            && param.Required is false));
    }

    /// <summary>
    /// Validates that input query parameters are not generated for Stored Procedures with
    /// no input parameters or no GET operations.
    /// </summary>
    [DataTestMethod]
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
        Assert.IsTrue(pathItem.Operations.Values.First().Parameters.IsNullOrEmpty());
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
}
