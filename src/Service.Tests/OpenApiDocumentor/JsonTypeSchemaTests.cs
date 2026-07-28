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
    /// OpenAPI document. DAB does nothing special for a JSON column - it is treated as a normal
    /// <c>string</c>, so it must be described with <c>type: string</c> and no custom <c>format</c>.
    /// This is the schema-discovery counterpart to the REST round-trip coverage in
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
        /// The <c>profiles.metadata</c> (json) column must appear in the OpenAPI schema as a plain
        /// string with no format - proving JSON is not given a bespoke scalar/format and is treated
        /// exactly like any other string column. (DAB''s OpenAPI documentor does not express column
        /// nullability on the property schema for any type, so that is not asserted here.)
        /// </summary>
        [TestMethod]
        public async Task JsonColumn_IsDescribedAsStringWithoutFormat()
        {
            OpenApiDocument doc = await GenerateProfileDocumentAsync();

            Assert.IsTrue(doc.Components.Schemas.ContainsKey("Profile"), "Schema should exist for the Profile entity.");

            OpenApiSchema profileSchema = doc.Components.Schemas["Profile"];
            Assert.IsTrue(profileSchema.Properties.ContainsKey("metadata"), "The json ''metadata'' column should be present in the schema.");

            OpenApiSchema metadataSchema = profileSchema.Properties["metadata"];
            Assert.AreEqual("string", metadataSchema.Type, "A json column must be described as a plain string (treated like any string column).");
            Assert.IsTrue(string.IsNullOrEmpty(metadataSchema.Format), "A json column must not carry a bespoke OpenAPI format.");
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
