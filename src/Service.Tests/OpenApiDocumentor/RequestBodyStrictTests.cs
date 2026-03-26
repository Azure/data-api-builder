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
        /// Validates that when request-body-strict is false, the redundant _NoAutoPK and _NoPK
        /// schemas are not generated. Operations reference the base entity schema instead.
        /// </summary>
        [TestMethod]
        public async Task RequestBodyStrict_False_OmitsRedundantSchemas()
        {
            OpenApiDocument doc = await GenerateDocumentWithPermissions(
                OpenApiTestBootstrap.CreateBasicPermissions(),
                requestBodyStrict: false);

            // _NoAutoPK and _NoPK schemas should not be generated when strict mode is off
            Assert.IsFalse(doc.Components.Schemas.ContainsKey("book_NoAutoPK"), "POST request body schema should not exist in non-strict mode");
            Assert.IsFalse(doc.Components.Schemas.ContainsKey("book_NoPK"), "PUT/PATCH request body schema should not exist in non-strict mode");

            // Base entity schema should still exist
            Assert.IsTrue(doc.Components.Schemas.ContainsKey("book"), "Base entity schema should exist");

            // Operations (POST/PUT/PATCH) should reference the base 'book' schema for their request bodies
            bool foundRequestBodyForWritableOperation = false;
            foreach (OpenApiPathItem pathItem in doc.Paths.Values)
            {
                foreach (KeyValuePair<OperationType, OpenApiOperation> operationKvp in pathItem.Operations)
                {
                    OperationType operationType = operationKvp.Key;
                    OpenApiOperation operation = operationKvp.Value;

                    if (operationType != OperationType.Post
                        && operationType != OperationType.Put
                        && operationType != OperationType.Patch)
                    {
                        continue;
                    }

                    if (operation.RequestBody is null)
                    {
                        continue;
                    }

                    if (!operation.RequestBody.Content.TryGetValue("application/json", out OpenApiMediaType mediaType)
                        || mediaType.Schema is null)
                    {
                        continue;
                    }

                    foundRequestBodyForWritableOperation = true;
                    OpenApiSchema schema = mediaType.Schema;

                    Assert.IsNotNull(schema.Reference, "Request body schema should reference a component schema when request-body-strict is false.");
                    Assert.AreEqual("book", schema.Reference.Id, "Request body should reference the base 'book' schema when request-body-strict is false.");
                    Assert.AreNotEqual("book_NoAutoPK", schema.Reference.Id, "Request body should not reference the 'book_NoAutoPK' schema when request-body-strict is false.");
                    Assert.AreNotEqual("book_NoPK", schema.Reference.Id, "Request body should not reference the 'book_NoPK' schema when request-body-strict is false.");
                }
            }

            Assert.IsTrue(foundRequestBodyForWritableOperation, "Expected at least one POST/PUT/PATCH operation with a JSON request body.");
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
