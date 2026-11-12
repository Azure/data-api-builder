// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class RuntimeConfigHelperTests
    {
        [TestMethod]
        public void OptionalRuntimeProperties_UseDocumentedDefaults()
        {
            RuntimeConfig config = CreateConfig(runtime: null);

            Assert.IsTrue(config.IsGraphQLEnabled);
            Assert.IsTrue(config.IsRestEnabled);
            Assert.IsTrue(config.IsMcpEnabled);
            Assert.IsTrue(config.IsHealthEnabled);
            Assert.IsTrue(config.IsUnauthenticatedIdentityProvider);
            Assert.IsFalse(config.IsStaticWebAppsIdentityProvider);
            Assert.IsFalse(config.IsAppServiceIdentityProvider);
            Assert.AreEqual(RestRuntimeOptions.DEFAULT_PATH, config.RestPath);
            Assert.AreEqual(GraphQLRuntimeOptions.DEFAULT_PATH, config.GraphQLPath);
            Assert.AreEqual(McpRuntimeOptions.DEFAULT_PATH, config.McpPath);
            Assert.IsTrue(config.AllowIntrospection);
            Assert.IsTrue(config.EnableAggregation);
            Assert.IsFalse(config.EnableDwNto1JoinOpt);
            Assert.AreEqual(0, config.AllowedRolesForHealth.Count);
            Assert.AreEqual(EntityCacheOptions.DEFAULT_TTL_SECONDS, config.CacheTtlSecondsForHealthReport);
        }

        [DataTestMethod]
        [DataRow("StaticWebApps", true, false, false)]
        [DataRow("AppService", false, true, false)]
        [DataRow("Unauthenticated", false, false, true)]
        [DataRow("Custom", false, false, false)]
        public void IdentityProviderProperties_AreCaseInsensitive(
            string provider,
            bool staticWebApps,
            bool appService,
            bool unauthenticated)
        {
            RuntimeOptions runtime = new(
                Rest: new RestRuntimeOptions(Enabled: true, Path: "/rest"),
                GraphQL: new GraphQLRuntimeOptions(Enabled: true, Path: "/gql", AllowIntrospection: false),
                Mcp: new McpRuntimeOptions(Enabled: true, Path: "/tools"),
                Host: new HostOptions(null, new AuthenticationOptions(provider)));
            RuntimeConfig config = CreateConfig(runtime);

            Assert.AreEqual(staticWebApps, config.IsStaticWebAppsIdentityProvider);
            Assert.AreEqual(appService, config.IsAppServiceIdentityProvider);
            Assert.AreEqual(unauthenticated, config.IsUnauthenticatedIdentityProvider);
            Assert.AreEqual("/rest", config.RestPath);
            Assert.AreEqual("/gql", config.GraphQLPath);
            Assert.AreEqual("/tools", config.McpPath);
            Assert.IsFalse(config.AllowIntrospection);
        }

        [TestMethod]
        public void CosmosDisablesRestEvenWhenRuntimeEnablesIt()
        {
            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.CosmosDB_NoSQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));

            Assert.IsFalse(config.IsRestEnabled);
        }

        /// <summary>
        /// Verifies omitted API sections remain enabled while configured health roles and cache lifetime flow through runtime accessors.
        /// </summary>
        [TestMethod]
        public void RuntimeProperties_EvaluateConfiguredSectionsAndHealthValues()
        {
            HashSet<string> roles = new() { "reader" };
            RuntimeOptions runtime = new(
                Rest: null,
                GraphQL: null,
                Mcp: null,
                Host: new HostOptions(null, null),
                Health: new RuntimeHealthCheckConfig(enabled: true, roles, cacheTtlSeconds: 17));
            RuntimeConfig config = CreateConfig(runtime);

            Assert.IsTrue(config.IsRestEnabled);
            Assert.IsTrue(config.IsGraphQLEnabled);
            Assert.IsTrue(config.IsMcpEnabled);
            Assert.IsTrue(config.IsHealthEnabled);
            Assert.IsTrue(config.AllowedRolesForHealth.SetEquals(roles));
            Assert.AreEqual(17, config.CacheTtlSecondsForHealthReport);
        }

        /// <summary>
        /// Verifies the DW to-one join optimization remains disabled until both GraphQL options and their feature flags are present and enabled.
        /// </summary>
        [TestMethod]
        public void EnableDwNto1JoinOpt_EvaluatesEachNestedConfigurationState()
        {
            RuntimeConfig missingGraphQL = CreateConfig(new RuntimeOptions(null, null, null, null));
            RuntimeConfig missingFlags = CreateConfig(new RuntimeOptions(
                null,
                new GraphQLRuntimeOptions { FeatureFlags = null! },
                null,
                null));
            RuntimeConfig enabled = CreateConfig(new RuntimeOptions(
                null,
                new GraphQLRuntimeOptions
                {
                    FeatureFlags = new FeatureFlags { EnableDwNto1JoinQueryOptimization = true }
                },
                null,
                null));

            Assert.IsFalse(missingGraphQL.EnableDwNto1JoinOpt);
            Assert.IsFalse(missingFlags.EnableDwNto1JoinOpt);
            Assert.IsTrue(enabled.EnableDwNto1JoinOpt);
        }

        /// <summary>
        /// Verifies data-source and entity indexes stay coherent across lookup, replacement, path registration, and generated-entity removal.
        /// </summary>
        [TestMethod]
        public void DataSourceAndEntityMaps_SupportLookupUpdateAndPathOperations()
        {
            Dictionary<string, Entity> entities = new() { ["Book"] = CreateEntity("books") };
            DataSource original = new(DatabaseType.MSSQL, "old");
            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: original,
                Entities: new RuntimeEntities(entities));
            string defaultName = config.DefaultDataSourceName;

            Assert.AreSame(original, config.GetDataSourceFromDataSourceName(defaultName));
            Assert.AreSame(original, config.GetDataSourceFromEntityName("Book"));
            Assert.AreEqual(defaultName, config.GetDataSourceNameFromEntityName("Book"));
            Assert.IsTrue(config.CheckDataSourceExists(defaultName));
            Assert.AreEqual(1, config.ListAllDataSources().Count());
            Assert.AreEqual(defaultName, config.GetDataSourceNamesToDataSourcesIterator().Single().Key);

            DataSource replacement = new(DatabaseType.PostgreSQL, "new");
            config.UpdateDataSourceNameToDataSource(defaultName, replacement);
            Assert.AreSame(replacement, config.GetDataSourceFromDataSourceName(defaultName));

            Assert.IsTrue(config.TryAddEntityPathNameToEntityName("books", "Book"));
            Assert.IsFalse(config.TryAddEntityPathNameToEntityName("books", "Other"));
            Assert.IsTrue(config.TryGetEntityNameFromPath("books", out string? entityName));
            Assert.AreEqual("Book", entityName);
            Assert.IsFalse(config.TryGetEntityNameFromPath("missing", out _));

            Assert.IsTrue(config.TryAddEntityNameToDataSourceName("Author"));
            Assert.IsFalse(config.TryAddEntityNameToDataSourceName("Author"));
            Assert.IsTrue(config.RemoveGeneratedAutoentityNameFromDataSourceName("Author"));
            Assert.IsFalse(config.RemoveGeneratedAutoentityNameFromDataSourceName("Author"));
        }

        [TestMethod]
        public void MappingLookups_RejectUnknownNames()
        {
            RuntimeConfig config = CreateConfig(runtime: null);

            Assert.ThrowsException<DataApiBuilderException>(() => config.GetDataSourceFromDataSourceName("missing"));
            Assert.ThrowsException<DataApiBuilderException>(() => config.UpdateDataSourceNameToDataSource("missing", new DataSource(DatabaseType.MSSQL, string.Empty)));
            Assert.ThrowsException<DataApiBuilderException>(() => config.GetDataSourceNameFromEntityName("missing"));
            Assert.ThrowsException<DataApiBuilderException>(() => config.GetDataSourceFromEntityName("missing"));
            Assert.ThrowsException<DataApiBuilderException>(() => config.GetDataSourceNameFromAutoentityName("missing"));
            Assert.IsFalse(config.TryAddGeneratedAutoentityNameToDataSourceName("Generated", "missing"));
        }

        [TestMethod]
        public void UpdateDefaultDataSourceName_RekeysDataSourceAndEntities()
        {
            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, "connection"),
                Entities: new RuntimeEntities(new Dictionary<string, Entity> { ["Book"] = CreateEntity("books") }));

            config.UpdateDefaultDataSourceName("stable-name");

            Assert.AreEqual("stable-name", config.DefaultDataSourceName);
            Assert.AreEqual("stable-name", config.GetDataSourceNameFromEntityName("Book"));
            Assert.IsTrue(config.CheckDataSourceExists("stable-name"));
        }

        [TestMethod]
        public void UpdateDefaultDataSourceName_RejectsDuplicateName()
        {
            DataSource original = new(DatabaseType.MSSQL, string.Empty);
            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: original,
                Runtime: null!,
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
                DefaultDataSourceName: "original",
                DataSourceNameToDataSource: new Dictionary<string, DataSource>
                {
                    ["original"] = original,
                    ["duplicate"] = new DataSource(DatabaseType.MySQL, string.Empty)
                },
                EntityNameToDataSourceName: new Dictionary<string, string>());

            Assert.ThrowsException<DataApiBuilderException>(() => config.UpdateDefaultDataSourceName("duplicate"));
        }

        /// <summary>
        /// Verifies paging, response-size, logging, hot-reload, multiple-create, and JSON fallbacks remain stable when runtime options are omitted.
        /// </summary>
        [TestMethod]
        public void RuntimeUtilityDefaults_AreStable()
        {
            RuntimeConfig config = CreateConfig(runtime: null);

            Assert.IsFalse(config.IsDevelopmentMode());
            Assert.IsFalse(RuntimeConfig.IsHotReloadable());
            Assert.IsFalse(config.IsMultipleCreateOperationEnabled());
            Assert.AreEqual(PaginationOptions.DEFAULT_PAGE_SIZE, config.DefaultPageSize());
            Assert.AreEqual(PaginationOptions.MAX_PAGE_SIZE, config.MaxPageSize());
            Assert.IsFalse(config.NextLinkRelative());
            Assert.AreEqual(HostOptions.MAX_RESPONSE_LENGTH_DAB_ENGINE_MB, config.MaxResponseSizeMB());
            Assert.IsFalse(config.MaxResponseSizeLogicEnabled());
            Assert.IsTrue(config.IsLogLevelNull());
            Assert.IsFalse(config.HasExplicitLogLevel());
            Assert.IsFalse(string.IsNullOrWhiteSpace(config.ToJson()));
        }

        [TestMethod]
        public void ExplicitMappingsConstructor_TracksSqlAndCosmosUsage()
        {
            DataSource sql = new(DatabaseType.MSSQL, string.Empty);
            DataSource cosmos = new(DatabaseType.CosmosDB_NoSQL, string.Empty);
            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: sql,
                Runtime: null!,
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
                DefaultDataSourceName: "sql",
                DataSourceNameToDataSource: new Dictionary<string, DataSource>
                {
                    ["sql"] = sql,
                    ["cosmos"] = cosmos
                },
                EntityNameToDataSourceName: new Dictionary<string, string>());

            Assert.IsTrue(config.SqlDataSourceUsed);
            Assert.IsTrue(config.CosmosDataSourceUsed);
        }

        private static RuntimeConfig CreateConfig(RuntimeOptions? runtime) => new(
            Schema: string.Empty,
            DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
            Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
            Runtime: runtime);

        private static Entity CreateEntity(string source) => new(
            Source: new EntitySource(source, EntitySourceType.Table, null, null),
            GraphQL: null,
            Fields: null,
            Rest: null,
            Permissions: Array.Empty<EntityPermission>(),
            Mappings: null,
            Relationships: null,
            Mcp: null);
    }
}
