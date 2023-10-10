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
/// Integration tests validating correct path and query parameters are created
/// for different entities in DAB's REST API endpoint.
/// </summary>
[TestCategory(TestCategory.MSSQL)]
[TestClass]
public class ParameterValidationTests
{
    private const string CUSTOM_CONFIG = "parameter-config.MsSql.json";
    private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;

    /// <summary>
    /// Validates the path parameters are correctly generated for GET methods.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="objectName">The name of the database object.</param>
    /// <param name="entitySourceType">The source type of the entity.</param>
    /// <returns></returns>
    [DataTestMethod]
    [DataRow("BooksTable", "books", EntitySourceType.Table, DisplayName = "Table with path parameter id")]
    [DataRow("BooksView", "books_view_all", EntitySourceType.View, DisplayName = "View with path parameter id")]
    public async Task ValidatePathParametersForTablesAndViews(string entityName, string objectName, EntitySourceType entitySourceType)
    {
        EntitySource entitySource = new(Object: objectName, entitySourceType, null, null);
        OpenApiDocument openApiDocument = await GenerateOpenApiDocumentForGivenEntity(entityName, entitySource);

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
                // Get All method with path /entityName, will have no path parameters.
                Assert.IsTrue(pathItem.Parameters.Count is 0);
            }
        }
    }

    /// <summary>
    /// Validates that Query parameters are correctly generated for GET methods in Table/Views.
    /// $select, $filter, $orderby, $first, $after are the query parameters.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="objectName">The name of the database object.</param>
    /// <param name="entitySourceType">The source type of the entity.</param>
    /// <returns></returns>
    [DataTestMethod]
    [DataRow("BooksTable", "books", EntitySourceType.Table, DisplayName = "Table with query parameters")]
    [DataRow("BooksView", "books_view_all", EntitySourceType.View, DisplayName = "View with query parameters")]
    public async Task ValidateQueryParametersForTablesAndViews(string entityName, string objectName, EntitySourceType entitySourceType)
    {
        EntitySource entitySource = new(Object: objectName, entitySourceType, null, null);
        OpenApiDocument openApiDocument = await GenerateOpenApiDocumentForGivenEntity(entityName, entitySource);

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
    /// Validates that Query parameters are correctly generated for Stored Procedures.
    /// Query parameters for stored procedures will be generated for each operation irrespective of its Operation Type.
    /// If a parameter default value is not provided in the config, it will marked as required.
    /// </summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="objectName">The name of the database object.</param>
    /// <returns></returns>
    [DataTestMethod]
    [DataRow("CountBooks", "count_books", DisplayName = "StoredProcedure with no parameters")]
    [DataRow("UpdateBookTitle", "update_book_title", DisplayName = "StoredProcedure with parameters")]
    public async Task ValidateQueryParametersForStoredProcedures(string entityName, string objectName)
    {
        Dictionary<string, object> parameterDefaults = null;
        if (entityName is "UpdateBookTitle")
        {
            // Adding a parameter default value.
            parameterDefaults = new Dictionary<string, object> { { "title", "Test Title" } };
        }

        EntitySource entitySource = new(Object: objectName, EntitySourceType.StoredProcedure, parameterDefaults, null);
        OpenApiDocument openApiDocument = await GenerateOpenApiDocumentForGivenEntity(entityName, entitySource);

        OpenApiPathItem pathItem = openApiDocument.Paths.First().Value;
        foreach (OpenApiOperation operation in pathItem.Operations.Values)
        {
            if (entityName.Equals("UpdateBookTitle"))
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
    private async static Task<OpenApiDocument> GenerateOpenApiDocumentForGivenEntity(string entityName, EntitySource entitySource)
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
        return await OpenApiTestBootstrap.GenerateOpenApiDocument(
            runtimeEntities: runtimeEntities,
            configFileName: CUSTOM_CONFIG,
            databaseEnvironment: MSSQL_ENVIRONMENT);
    }
}
