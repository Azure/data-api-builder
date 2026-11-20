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
    /// Validates how a SQL Server 2025 native <c>json</c> column is surfaced in the generated
    /// OpenAPI document. DAB treats a json column as a normal <c>string</c> for input and output,
    /// so it must be described with <c>type: string</c> and no custom <c>format</c> in the response
    /// schema as well as the POST / PUT / PATCH request-body schemas. This is the schema-discovery
    /// counterpart to the REST round-trip coverage in
    /// <see cref="SqlTests.RestApiTests.MsSqlRestJsonTypesTests"/>.
    /// NOTE: The native JSON data type requires SQL Server 2025 / Azure SQL.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class JsonTypeSchemaTests
    {
        private const string CONFIG_FILE = "json-type-openapi-config.MsSql.json";
        private const string DB_ENV = TestCategory.MSSQL;

        /// <summary>
        /// The <c>profiles.metadata</c> (json) column must be described as a plain string with no format
        /// in the response schema and in both request-body schemas (POST => <c>Profile_NoAutoPK</c>,
        /// PUT/PATCH => <c>Profile_NoPK</c>) - proving json is treated like a normal string for both input
        /// and output, with no bespoke scalar/format. (DAB does not express column nullability on the
        /// property schema for any type, so that is not asserted here.)
        /// </summary>
        [TestMethod]
        public async Task JsonColumn_IsDescribedAsStringWithoutFormat_InResponseAndRequestBodies()
        {
            OpenApiDocument doc = await GenerateProfileDocumentAsync();

            // Response body schema, plus the POST and PUT/PATCH request-body schemas.
            AssertMetadataIsPlainString(doc, "Profile");
            AssertMetadataIsPlainString(doc, "Profile_NoAutoPK");
            AssertMetadataIsPlainString(doc, "Profile_NoPK");
        }

        /// <summary>
        /// Asserts the named component schema exposes <c>metadata</c> as a plain string with no format.
        /// </summary>
        private static void AssertMetadataIsPlainString(OpenApiDocument doc, string schemaName)
        {
            Assert.IsTrue(doc.Components.Schemas.ContainsKey(schemaName), $"Schema {schemaName} should exist.");

            OpenApiSchema schema = doc.Components.Schemas[schemaName];
            Assert.IsTrue(schema.Properties.ContainsKey("metadata"), $"The json metadata column should be present in {schemaName}.");

            OpenApiSchema metadataSchema = schema.Properties["metadata"];
            Assert.AreEqual("string", metadataSchema.Type, $"A json column must be described as a plain string in {schemaName}.");
            Assert.IsTrue(string.IsNullOrEmpty(metadataSchema.Format), $"A json column must not carry a bespoke OpenAPI format in {schemaName}.");
        }

        /// <summary>
        /// Builds an OpenAPI document for a Profile entity sourced from the <c>profiles</c> table,
        /// with REST + GraphQL enabled and anonymous/authenticated CRUD permissions.
        /// </summary>
        private static async Task<OpenApiDocument> GenerateProfileDocumentAsync()
        {
            Entity entity = new(
                Source: new("profiles", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new("Profile", "Profiles", true),
                Rest: new(EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null);

            RuntimeEntities entities = new(new Dictionary<string, Entity> { { "Profile", entity } });
            return await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(entities, CONFIG_FILE, DB_ENV);
        }
    }
}
