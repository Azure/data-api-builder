// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.OpenApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OpenApiIntegration
{
    /// <summary>
    /// Integration tests validating that OpenAPI document schemas respect the request-body-strict configuration.
    /// When request-body-strict is false, request body schemas should have additionalProperties set to true
    /// to indicate that extra fields in request bodies are allowed and will be ignored.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class RequestBodyStrictTests
    {
        private const string CUSTOM_CONFIG = "request-body-strict.MsSql.json";
        private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;

        /// <summary>
        /// Validates that when request-body-strict is true (default), the OpenAPI request body schemas have
        /// additionalProperties set to false, indicating that extra fields are not allowed.
        /// The response schema (Book) should always have additionalProperties false.
        /// </summary>
        [TestMethod]
        public async Task OpenApiSchema_WhenRequestBodyStrictTrue_AdditionalPropertiesIsFalse()
        {
            // Arrange
            RuntimeEntities runtimeEntities = CreateBookRuntimeEntities();
            RestRuntimeOptions restOptions = new(Enabled: true, Path: "/api", RequestBodyStrict: true);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT,
                restOptions: restOptions);

            // Assert - Response schema (Book) should always be false
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("Book"), "Schema 'Book' should exist.");
            Assert.IsFalse(openApiDocument.Components.Schemas["Book"].AdditionalPropertiesAllowed,
                "Response schema 'Book' additionalProperties should always be false.");

            // Assert - Request body schemas should be false when strict
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("Book_NoAutoPK"), "Schema 'Book_NoAutoPK' should exist.");
            Assert.IsFalse(openApiDocument.Components.Schemas["Book_NoAutoPK"].AdditionalPropertiesAllowed,
                "Request body schema 'Book_NoAutoPK' additionalProperties should be false when strict.");

            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("Book_NoPK"), "Schema 'Book_NoPK' should exist.");
            Assert.IsFalse(openApiDocument.Components.Schemas["Book_NoPK"].AdditionalPropertiesAllowed,
                "Request body schema 'Book_NoPK' additionalProperties should be false when strict.");
        }

        /// <summary>
        /// Validates that when request-body-strict is false, the OpenAPI request body schemas have
        /// additionalProperties set to true, indicating that extra fields are allowed.
        /// The response schema (Book) should still have additionalProperties false.
        /// This addresses GitHub issue #2947 and #1838.
        /// </summary>
        [TestMethod]
        public async Task OpenApiSchema_WhenRequestBodyStrictFalse_AdditionalPropertiesIsTrue()
        {
            // Arrange
            RuntimeEntities runtimeEntities = CreateBookRuntimeEntities();
            RestRuntimeOptions restOptions = new(Enabled: true, Path: "/api", RequestBodyStrict: false);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT,
                restOptions: restOptions);

            // Assert - Response schema (Book) should always be false, even when non-strict
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("Book"), "Schema 'Book' should exist.");
            Assert.IsFalse(openApiDocument.Components.Schemas["Book"].AdditionalPropertiesAllowed,
                "Response schema 'Book' additionalProperties should always be false.");

            // Assert - Request body schemas should be true when non-strict
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("Book_NoAutoPK"), "Schema 'Book_NoAutoPK' should exist.");
            Assert.IsTrue(openApiDocument.Components.Schemas["Book_NoAutoPK"].AdditionalPropertiesAllowed,
                "Request body schema 'Book_NoAutoPK' additionalProperties should be true when non-strict.");

            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("Book_NoPK"), "Schema 'Book_NoPK' should exist.");
            Assert.IsTrue(openApiDocument.Components.Schemas["Book_NoPK"].AdditionalPropertiesAllowed,
                "Request body schema 'Book_NoPK' additionalProperties should be true when non-strict.");
        }

        /// <summary>
        /// Creates RuntimeEntities with a single Book entity backed by the books table.
        /// </summary>
        private static RuntimeEntities CreateBookRuntimeEntities()
        {
            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null);

            Dictionary<string, Entity> entities = new()
            {
                { "Book", entity }
            };

            return new RuntimeEntities(entities);
        }
    }
}
