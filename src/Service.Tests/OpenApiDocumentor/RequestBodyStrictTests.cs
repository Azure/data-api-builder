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
    /// Tests validating OpenAPI schema correctly applies request-body-strict setting.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class RequestBodyStrictTests
    {
        private const string CONFIG_FILE = "request-body-strict-config.MsSql.json";
        private const string DB_ENV = TestCategory.MSSQL;

        /// <summary>
        /// Validates that when request-body-strict is true (default), request body schemas
        /// have additionalProperties set to false.
        /// </summary>
        [TestMethod]
        public async Task RequestBodyStrict_True_DisallowsExtraFields()
        {
            OpenApiDocument doc = await GenerateDocumentWithPermissions(
                OpenApiTestBootstrap.CreateBasicPermissions(),
                requestBodyStrict: true);

            // Request body schemas should have additionalProperties = false
            Assert.IsTrue(doc.Components.Schemas.ContainsKey("book_NoAutoPK"), "POST request body schema should exist");
            Assert.IsFalse(doc.Components.Schemas["book_NoAutoPK"].AdditionalPropertiesAllowed, "POST request body should not allow extra fields in strict mode");

            Assert.IsTrue(doc.Components.Schemas.ContainsKey("book_NoPK"), "PUT/PATCH request body schema should exist");
            Assert.IsFalse(doc.Components.Schemas["book_NoPK"].AdditionalPropertiesAllowed, "PUT/PATCH request body should not allow extra fields in strict mode");

            // Response body schema should allow extra fields (not a request body)
            Assert.IsTrue(doc.Components.Schemas.ContainsKey("book"), "Response body schema should exist");
            Assert.IsTrue(doc.Components.Schemas["book"].AdditionalPropertiesAllowed, "Response body should allow extra fields");
        }

        /// <summary>
        /// Validates that when request-body-strict is false, request body schemas
        /// have additionalProperties set to true.
        /// </summary>
        [TestMethod]
        public async Task RequestBodyStrict_False_AllowsExtraFields()
        {
            OpenApiDocument doc = await GenerateDocumentWithPermissions(
                OpenApiTestBootstrap.CreateBasicPermissions(),
                requestBodyStrict: false);

            // Request body schemas should have additionalProperties = true
            Assert.IsTrue(doc.Components.Schemas.ContainsKey("book_NoAutoPK"), "POST request body schema should exist");
            Assert.IsTrue(doc.Components.Schemas["book_NoAutoPK"].AdditionalPropertiesAllowed, "POST request body should allow extra fields in non-strict mode");

            Assert.IsTrue(doc.Components.Schemas.ContainsKey("book_NoPK"), "PUT/PATCH request body schema should exist");
            Assert.IsTrue(doc.Components.Schemas["book_NoPK"].AdditionalPropertiesAllowed, "PUT/PATCH request body should allow extra fields in non-strict mode");
        }

        private static async Task<OpenApiDocument> GenerateDocumentWithPermissions(EntityPermission[] permissions, bool? requestBodyStrict = null)
        {
            Entity entity = new(
                Source: new("books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(null, null, false),
                Rest: new(EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: permissions,
                Mappings: null,
                Relationships: null);

            RuntimeEntities entities = new(new Dictionary<string, Entity> { { "book", entity } });
            return await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(entities, CONFIG_FILE, DB_ENV, requestBodyStrict);
        }
    }
}
