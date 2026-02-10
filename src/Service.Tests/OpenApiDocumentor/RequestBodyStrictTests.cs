// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.OpenApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OpenApiIntegration
{
    /// <summary>
    /// Integration tests validating that OpenAPI document schemas respect the request-body-strict configuration.
    /// When request-body-strict is false, schemas should have additionalProperties set to true to indicate
    /// that extra fields in request bodies are allowed and will be ignored.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class RequestBodyStrictTests
    {
        private const string CUSTOM_CONFIG = "request-body-strict.MsSql.json";
        private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;

        /// <summary>
        /// Validates that when request-body-strict is true (default), the OpenAPI schema has
        /// additionalProperties set to false, indicating that extra fields are not allowed.
        /// </summary>
        [TestMethod]
        public async Task OpenApiSchema_WhenRequestBodyStrictTrue_AdditionalPropertiesIsFalse()
        {
            // Arrange
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

            RuntimeEntities runtimeEntities = new(entities);

            // request-body-strict: true (default)
            RestRuntimeOptions restOptions = new(Enabled: true, Path: "/api", RequestBodyStrict: true);

            // Act - Create OpenApi document
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT,
                restOptions: restOptions);

            // Assert - Validate that Book schema has additionalProperties set to false
            Assert.IsTrue(
                openApiDocument.Components.Schemas.ContainsKey("Book"),
                "Schema 'Book' should exist in OpenAPI document.");

            OpenApiSchema bookSchema = openApiDocument.Components.Schemas["Book"];
            Assert.IsFalse(
                bookSchema.AdditionalPropertiesAllowed,
                "When request-body-strict is true, additionalProperties should be false.");

            // Validate _NoAutoPK and _NoPK schemas as well
            Assert.IsTrue(
                openApiDocument.Components.Schemas.ContainsKey("Book_NoAutoPK"),
                "Schema 'Book_NoAutoPK' should exist in OpenAPI document.");
            Assert.IsFalse(
                openApiDocument.Components.Schemas["Book_NoAutoPK"].AdditionalPropertiesAllowed,
                "When request-body-strict is true, Book_NoAutoPK additionalProperties should be false.");

            Assert.IsTrue(
                openApiDocument.Components.Schemas.ContainsKey("Book_NoPK"),
                "Schema 'Book_NoPK' should exist in OpenAPI document.");
            Assert.IsFalse(
                openApiDocument.Components.Schemas["Book_NoPK"].AdditionalPropertiesAllowed,
                "When request-body-strict is true, Book_NoPK additionalProperties should be false.");
        }

        /// <summary>
        /// Validates that when request-body-strict is false, the OpenAPI schema has
        /// additionalProperties set to true, indicating that extra fields are allowed.
        /// This addresses GitHub issue #2947 and #1838.
        /// </summary>
        [TestMethod]
        public async Task OpenApiSchema_WhenRequestBodyStrictFalse_AdditionalPropertiesIsTrue()
        {
            // Arrange
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

            RuntimeEntities runtimeEntities = new(entities);

            // request-body-strict: false (non-strict mode)
            RestRuntimeOptions restOptions = new(Enabled: true, Path: "/api", RequestBodyStrict: false);

            // Act - Create OpenApi document
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT,
                restOptions: restOptions);

            // Assert - Validate that Book schema has additionalProperties set to true
            Assert.IsTrue(
                openApiDocument.Components.Schemas.ContainsKey("Book"),
                "Schema 'Book' should exist in OpenAPI document.");

            OpenApiSchema bookSchema = openApiDocument.Components.Schemas["Book"];
            Assert.IsTrue(
                bookSchema.AdditionalPropertiesAllowed,
                "When request-body-strict is false, additionalProperties should be true to indicate extra fields are allowed.");

            // Validate _NoAutoPK and _NoPK schemas as well
            Assert.IsTrue(
                openApiDocument.Components.Schemas.ContainsKey("Book_NoAutoPK"),
                "Schema 'Book_NoAutoPK' should exist in OpenAPI document.");
            Assert.IsTrue(
                openApiDocument.Components.Schemas["Book_NoAutoPK"].AdditionalPropertiesAllowed,
                "When request-body-strict is false, Book_NoAutoPK additionalProperties should be true.");

            Assert.IsTrue(
                openApiDocument.Components.Schemas.ContainsKey("Book_NoPK"),
                "Schema 'Book_NoPK' should exist in OpenAPI document.");
            Assert.IsTrue(
                openApiDocument.Components.Schemas["Book_NoPK"].AdditionalPropertiesAllowed,
                "When request-body-strict is false, Book_NoPK additionalProperties should be true.");
        }
    }
}
