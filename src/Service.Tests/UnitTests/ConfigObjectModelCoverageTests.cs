// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class ConfigObjectModelCoverageTests
    {
        [TestMethod]
        public void EntityActionPolicy_ProcessesFieldsAndRejectsNullDatabasePolicy()
        {
            Assert.ThrowsException<NullReferenceException>(() => new EntityActionPolicy().ProcessedDatabaseFields());
            Assert.AreEqual(
                "id eq 1 and snake_case eq 2",
                new EntityActionPolicy(Database: "@item.id eq 1 and @item.snake_case eq 2").ProcessedDatabaseFields());
        }

        [TestMethod]
        public void RuntimeOptions_DisabledFeaturesReturnFalse()
        {
            RuntimeOptions options = new(
                Rest: new RestRuntimeOptions(Enabled: false),
                GraphQL: new GraphQLRuntimeOptions(Enabled: false),
                Mcp: new McpRuntimeOptions(Enabled: false, Path: "/mcp", DmlTools: null),
                Host: null,
                Health: new RuntimeHealthCheckConfig(enabled: false));

            Assert.IsFalse(options.IsCachingEnabled);
            Assert.IsFalse(options.IsRestEnabled);
            Assert.IsFalse(options.IsGraphQLEnabled);
            Assert.IsFalse(options.IsMcpEnabled);
            Assert.IsFalse(options.IsHealthCheckEnabled);
            Assert.IsFalse(options.IsEmbeddingsConfigured);
        }

        [TestMethod]
        public void ChildConfigMetadata_RecordPropertiesRoundTrip()
        {
            HashSet<string> entities = new() { "Book" };
            HashSet<string> autoentities = new() { "AutoBook" };
            ChildConfigMetadata metadata = new("child.json", entities, autoentities, HasDataSource: true);

            Assert.AreEqual("child.json", metadata.FileName);
            Assert.AreSame(entities, metadata.EntityNames);
            Assert.AreSame(autoentities, metadata.AutoentityDefinitionNames);
            Assert.IsTrue(metadata.HasDataSource);
        }

        [TestMethod]
        public void EmbeddingsHealthCheck_DefaultConstructorUsesDefaults()
        {
            EmbeddingsHealthCheckConfig health = new();

            Assert.AreEqual(EmbeddingsHealthCheckConfig.DEFAULT_THRESHOLD_MS, health.ThresholdMs);
            Assert.AreEqual(EmbeddingsHealthCheckConfig.DEFAULT_TEST_TEXT, health.TestText);
            Assert.IsFalse(health.UserProvidedThresholdMs);
            Assert.IsFalse(health.UserProvidedTestText);
            Assert.IsFalse(health.UserProvidedExpectedDimensions);
        }

        [TestMethod]
        public void DatasourceHealthCheck_ConstructorsTrackDefaultsAndUserValues()
        {
            DatasourceHealthCheckConfig defaults = new();
            DatasourceHealthCheckConfig configured = new(enabled: true, name: "primary", thresholdMs: 42);

            Assert.IsTrue(defaults.ThresholdMs > 0);
            Assert.IsFalse(defaults.UserProvidedThresholdMs);
            Assert.AreEqual("primary", configured.Name);
            Assert.AreEqual(42, configured.ThresholdMs);
            Assert.IsTrue(configured.UserProvidedThresholdMs);
        }

        [TestMethod]
        public void EmbeddingsOptions_NullOptionalFeaturesUseDocumentedFallbacks()
        {
            EmbeddingsOptions options = new(EmbeddingProviderType.OpenAI, "https://example.com", "key");

            Assert.IsFalse(options.IsHealthCheckEnabled);
            Assert.IsFalse(options.IsEndpointEnabled);
            Assert.IsFalse(options.IsChunkingEnabled);
            Assert.IsTrue(options.IsCachingEnabled);
            Assert.IsFalse(options.IsLevel2CacheEnabled);
        }

        [TestMethod]
        public void EmbeddingsOptions_Level2CacheRequiresBothCacheLevels()
        {
            EmbeddingsOptions disabled = new(EmbeddingProviderType.OpenAI, "https://example.com", "key")
            {
                Cache = new EmbeddingsCacheOptions(Enabled: false, Level2: new EmbeddingsCacheLevel2Options(Enabled: true))
            };
            EmbeddingsOptions level2Disabled = new(EmbeddingProviderType.OpenAI, "https://example.com", "key")
            {
                Cache = new EmbeddingsCacheOptions(Enabled: true, Level2: new EmbeddingsCacheLevel2Options(Enabled: false))
            };
            EmbeddingsOptions enabled = new(EmbeddingProviderType.OpenAI, "https://example.com", "key")
            {
                Cache = new EmbeddingsCacheOptions(Enabled: true, Level2: new EmbeddingsCacheLevel2Options(Enabled: true))
            };

            Assert.IsFalse(disabled.IsLevel2CacheEnabled);
            Assert.IsFalse(level2Disabled.IsLevel2CacheEnabled);
            Assert.IsTrue(enabled.IsLevel2CacheEnabled);
        }

        [TestMethod]
        public void EmbeddingsEndpointOptions_DefaultConstructorDisablesEndpoint()
        {
            EmbeddingsEndpointOptions options = new();

            Assert.IsFalse(options.Enabled);
            Assert.IsFalse(options.UserProvidedEnabled);
        }

        [TestMethod]
        public void Entity_ConfiguredHealthValuesAreReturned()
        {
            Entity entity = CreateEntity(new EntityHealthCheckConfig(enabled: true, first: 7, thresholdMs: 42));

            Assert.AreEqual(7, entity.EntityFirst);
            Assert.AreEqual(42, entity.EntityThresholdMs);
        }

        [TestMethod]
        public void EntityRelationshipKey_EqualityHandlesNullIdentityAndValues()
        {
            EntityRelationshipKey key = new("Book", "publisher");

            Assert.IsFalse(key.Equals(null));
            Assert.IsTrue(key.Equals(key));
            Assert.IsTrue(key.Equals(new EntityRelationshipKey("Book", "publisher")));
            Assert.IsFalse(key.Equals(new EntityRelationshipKey("Book", "author")));
            Assert.IsFalse(key.Equals(new object()));
            Assert.AreEqual(key.GetHashCode(), new EntityRelationshipKey("Book", "publisher").GetHashCode());
        }

        [TestMethod]
        public void RuntimeAutoentities_GenericAndNonGenericEnumerationReturnEntries()
        {
            RuntimeAutoentities autoentities = new(new Dictionary<string, Autoentity>
            {
                ["Book"] = new Autoentity(Patterns: null, Template: null, Permissions: null)
            });

            Assert.AreEqual("Book", autoentities.Single().Key);
            Assert.IsTrue(((System.Collections.IEnumerable)autoentities).GetEnumerator().MoveNext());
        }

        [TestMethod]
        public void RuntimeEntities_MissingIndexerThrowsConfigurationError()
        {
            RuntimeEntities entities = new(new Dictionary<string, Entity>());

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(() => _ = entities["Missing"]);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, exception.SubStatusCode);
        }

        [TestMethod]
        public void DataSource_DefaultsAndTypedOptionsCoverFallbacks()
        {
            DataSource defaults = new(DatabaseType.MSSQL, string.Empty);
            Assert.IsTrue(defaults.IsDatasourceHealthEnabled);
            Assert.IsTrue(defaults.DatasourceThresholdMs > 0);
            Assert.IsFalse(defaults.IsUserDelegatedAuthEnabled);
            Assert.IsNull(defaults.GetTypedOptions<CosmosDbNoSQLDataSourceOptions>());

            DataSource wrongTypes = new(
                DatabaseType.MSSQL,
                string.Empty,
                new Dictionary<string, object?> { ["set-session-context"] = "not-a-bool" });
            Assert.IsFalse(wrongTypes.GetTypedOptions<MsSqlOptions>()!.SetSessionContext);
            Assert.ThrowsException<NotSupportedException>(() => wrongTypes.GetTypedOptions<UnsupportedOptions>());
            StringAssert.Contains(wrongTypes.DatabaseTypeNotSupportedMessage, DatabaseType.MSSQL.ToString());
        }

        [TestMethod]
        public void RuntimeConfig_MissingEntityCacheLookupsThrow()
        {
            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));

            Assert.ThrowsException<DataApiBuilderException>(() => config.GetEntityCacheEntryTtl("Missing"));
            Assert.ThrowsException<DataApiBuilderException>(() => config.GetEntityCacheEntryLevel("Missing"));
            Assert.ThrowsException<DataApiBuilderException>(() => config.IsEntityCachingEnabled("Missing"));
        }

        [TestMethod]
        public void RuntimeConfig_EntityCacheTtlOverridesGlobalDefault()
        {
            Entity entity = CreateEntity() with { Cache = new EntityCacheOptions(Enabled: true, TtlSeconds: 42) };
            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity> { ["Book"] = entity }));

            Assert.AreEqual(42, config.GetEntityCacheEntryTtl("Book"));
        }

        [TestMethod]
        public void RuntimeConfig_MultipleCreateEnabledForSupportedDatabase()
        {
            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
                Runtime: new RuntimeOptions(
                    Rest: new RestRuntimeOptions(),
                    GraphQL: new GraphQLRuntimeOptions(
                        MultipleMutationOptions: new MultipleMutationOptions(new MultipleCreateOptions(true))),
                    Mcp: null,
                    Host: null),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));

            Assert.IsTrue(config.IsMultipleCreateOperationEnabled());
        }

        private static Entity CreateEntity(EntityHealthCheckConfig? health = null)
        {
            return new Entity(
                Source: new EntitySource("books", EntitySourceType.Table, null, null),
                GraphQL: new EntityGraphQLOptions("Book", "Books"),
                Fields: null,
                Rest: new EntityRestOptions(),
                Permissions: Array.Empty<EntityPermission>(),
                Mappings: null,
                Relationships: null,
                Health: health);
        }

        private sealed class UnsupportedOptions : IDataSourceOptions
        {
        }
    }
}
