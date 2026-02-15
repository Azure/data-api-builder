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
    /// Integration tests validating correct keys are created for OpenApiDocument.Paths
    /// which represent the path used to access an entity in DAB's REST API endpoint.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class PathValidationTests
    {
        private const string CUSTOM_CONFIG = "path-config.MsSql.json";
        private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;

        // Error messages
        private const string PATH_GENERATION_ERROR = "Unexpected path value for entity in OpenAPI description document: ";

        /// <summary>
        /// Validates that the OpenApiDocument object's Paths property for an entity is generated
        /// with the entity's explicitly configured REST path, if set. Otherwise, the top level
        /// entity name is used.
        /// When OpenApiDocumentor.BuildPaths() is called, the entityBasePathComponent is created using
        /// the formula "/{entityRestPath}" where {entityRestPath} has no starting slashes and is either
        /// the entity name or the explicitly configured entity REST path
        /// </summary>
        /// <param name="entityName">Top level entity name defined in runtime config.</param>
        /// <param name="configuredRestPath">Entity's configured REST path.</param>
        /// <param name="expectedOpenApiPath">Expected path generated for OpenApiDocument.Paths with format: "/{entityRestPath}"</param>
        [DataRow("entity", "/customEntityPath", "/customEntityPath", DisplayName = "Entity REST path has leading slash - REST path override used.")]
        [DataRow("entity", "//customEntityPath", "/customEntityPath", DisplayName = "Entity REST path has two leading slashes - REST path override used.")]
        [DataRow("entity", "///customEntityPath", "/customEntityPath", DisplayName = "Entity REST path has many leading slashes - REST path override used.")]
        [DataRow("entity", "customEntityPath", "/customEntityPath", DisplayName = "Entity REST path has no leading slash(es) - REST path override used.")]
        [DataRow("entity", null, "/entity", DisplayName = "Entity REST path is null - top level entity name used.")]
        [DataTestMethod]
        public async Task ValidateEntityRestPath(string entityName, string configuredRestPath, string expectedOpenApiPath)
        {
            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS, Path: configuredRestPath),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null);

            Dictionary<string, Entity> entities = new()
            {
                { entityName, entity }
            };

            RuntimeEntities runtimeEntities = new(entities);
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // For a given table backed entity, there will be two paths:
            // 1. GetById path: "/customEntityPath/id/{id}"
            // 2. GetAll path:  "/customEntityPath"
            foreach (string actualPath in openApiDocument.Paths.Keys)
            {
                Assert.AreEqual(expected: true, actual: actualPath.StartsWith(expectedOpenApiPath), message: PATH_GENERATION_ERROR + actualPath);
            }
        }
    }
}
