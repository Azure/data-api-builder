// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for URI/policy validation helpers on <see cref="RuntimeConfigValidator"/> that
    /// operate purely on a <see cref="RuntimeConfig"/> (no database metadata required).
    /// </summary>
    [TestClass]
    public class RuntimeConfigValidatorUnitTests
    {
        #region IsValidDatabasePolicyForAction

        [TestMethod]
        public void IsValidDatabasePolicyForAction_CreateWithDatabasePolicy_ReturnsFalse()
        {
            EntityAction action = new(
                Action: EntityActionOperation.Create,
                Fields: null,
                Policy: new EntityActionPolicy(Database: "@item.id eq 1"));

            Assert.IsFalse(RuntimeConfigValidator.IsValidDatabasePolicyForAction(action));
        }

        [TestMethod]
        public void IsValidDatabasePolicyForAction_CreateWithoutPolicy_ReturnsTrue()
        {
            EntityAction action = new(Action: EntityActionOperation.Create, Fields: null, Policy: null);
            Assert.IsTrue(RuntimeConfigValidator.IsValidDatabasePolicyForAction(action));
        }

        [TestMethod]
        public void IsValidDatabasePolicyForAction_CreateWithEmptyDatabasePolicy_ReturnsTrue()
        {
            EntityAction action = new(
                Action: EntityActionOperation.Create,
                Fields: null,
                Policy: new EntityActionPolicy(Database: "  "));

            Assert.IsTrue(RuntimeConfigValidator.IsValidDatabasePolicyForAction(action));
        }

        [DataTestMethod]
        [DataRow(EntityActionOperation.Read)]
        [DataRow(EntityActionOperation.Update)]
        [DataRow(EntityActionOperation.Delete)]
        public void IsValidDatabasePolicyForAction_NonCreateWithDatabasePolicy_ReturnsTrue(EntityActionOperation operation)
        {
            EntityAction action = new(
                Action: operation,
                Fields: null,
                Policy: new EntityActionPolicy(Database: "@item.id eq 1"));

            Assert.IsTrue(RuntimeConfigValidator.IsValidDatabasePolicyForAction(action));
        }

        #endregion

        #region ValidateRestURI

        [TestMethod]
        public void ValidateRestURI_ValidPath_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            validator.ValidateRestURI(ConfigWith(rest: new RestRuntimeOptions(Enabled: true, Path: "/api")));
        }

        [DataTestMethod]
        [DataRow("apiWithoutLeadingSlash", DisplayName = "No leading slash")]
        [DataRow("/api v2", DisplayName = "Contains whitespace")]
        public void ValidateRestURI_InvalidPath_Throws(string path)
        {
            RuntimeConfigValidator validator = CreateValidator();

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateRestURI(ConfigWith(rest: new RestRuntimeOptions(Enabled: true, Path: path))));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        #endregion

        #region ValidateGraphQLURI

        [TestMethod]
        public void ValidateGraphQLURI_ValidPath_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            validator.ValidateGraphQLURI(ConfigWith(graphQL: new GraphQLRuntimeOptions(Path: "/graphql")));
        }

        [TestMethod]
        public void ValidateGraphQLURI_InvalidPath_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateGraphQLURI(ConfigWith(graphQL: new GraphQLRuntimeOptions(Path: "graphqlNoSlash"))));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        #endregion

        #region ValidateMcpUri

        [TestMethod]
        public void ValidateMcpUri_McpNotConfigured_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            validator.ValidateMcpUri(ConfigWith(mcp: null));
        }

        [TestMethod]
        public void ValidateMcpUri_ValidPath_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            validator.ValidateMcpUri(ConfigWith(mcp: new McpRuntimeOptions(Enabled: true, Path: "/mcp", DmlTools: null)));
        }

        [TestMethod]
        public void ValidateMcpUri_EmptyPath_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateMcpUri(ConfigWith(mcp: new McpRuntimeOptions(Enabled: true, Path: "", DmlTools: null))));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        [TestMethod]
        public void ValidateMcpUri_InvalidPath_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateMcpUri(ConfigWith(mcp: new McpRuntimeOptions(Enabled: true, Path: "mcpNoSlash", DmlTools: null))));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        #endregion

        #region ValidateAppInsightsTelemetryConnectionString

        [TestMethod]
        public void ValidateAppInsights_EnabledWithoutConnectionString_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();
            TelemetryOptions telemetry = new(ApplicationInsights: new ApplicationInsightsOptions(Enabled: true, ConnectionString: ""));

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateAppInsightsTelemetryConnectionString(ConfigWith(telemetry: telemetry)));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        [TestMethod]
        public void ValidateAppInsights_EnabledWithConnectionString_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            TelemetryOptions telemetry = new(ApplicationInsights: new ApplicationInsightsOptions(Enabled: true, ConnectionString: "InstrumentationKey=abc"));

            validator.ValidateAppInsightsTelemetryConnectionString(ConfigWith(telemetry: telemetry));
        }

        [TestMethod]
        public void ValidateAppInsights_Disabled_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            TelemetryOptions telemetry = new(ApplicationInsights: new ApplicationInsightsOptions(Enabled: false, ConnectionString: null));

            validator.ValidateAppInsightsTelemetryConnectionString(ConfigWith(telemetry: telemetry));
        }

        #endregion

        #region ValidateAzureLogAnalyticsAuth

        [TestMethod]
        public void ValidateAzureLogAnalyticsAuth_EnabledWithoutAuth_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();
            TelemetryOptions telemetry = new(AzureLogAnalytics: new AzureLogAnalyticsOptions { Enabled = true, Auth = null });

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateAzureLogAnalyticsAuth(ConfigWith(telemetry: telemetry)));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        [TestMethod]
        public void ValidateAzureLogAnalyticsAuth_EnabledWithCompleteAuth_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            AzureLogAnalyticsAuthOptions auth = new()
            {
                CustomTableName = "DabLogs",
                DcrImmutableId = "dcr-123",
                DceEndpoint = "https://dce.example.com"
            };
            TelemetryOptions telemetry = new(AzureLogAnalytics: new AzureLogAnalyticsOptions { Enabled = true, Auth = auth });

            validator.ValidateAzureLogAnalyticsAuth(ConfigWith(telemetry: telemetry));
        }

        [TestMethod]
        public void ValidateAzureLogAnalyticsAuth_Disabled_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            TelemetryOptions telemetry = new(AzureLogAnalytics: new AzureLogAnalyticsOptions { Enabled = false, Auth = null });

            validator.ValidateAzureLogAnalyticsAuth(ConfigWith(telemetry: telemetry));
        }

        #endregion

        #region ValidateFileSinkPath

        [TestMethod]
        public void ValidateFileSinkPath_EnabledWithoutPath_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();
            TelemetryOptions telemetry = new(File: new FileSinkOptions { Enabled = true, Path = "" });

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateFileSinkPath(ConfigWith(telemetry: telemetry)));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        [TestMethod]
        public void ValidateFileSinkPath_EnabledWithValidPath_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            TelemetryOptions telemetry = new(File: new FileSinkOptions { Enabled = true, Path = "logs/dab-log.txt" });

            validator.ValidateFileSinkPath(ConfigWith(telemetry: telemetry));
        }

        [TestMethod]
        public void ValidateFileSinkPath_Disabled_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            TelemetryOptions telemetry = new(File: new FileSinkOptions { Enabled = false, Path = "logs/dab-log.txt" });

            validator.ValidateFileSinkPath(ConfigWith(telemetry: telemetry));
        }

        [TestMethod]
        public void ValidateFileSinkPath_PathLongerThanRecommendation_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            string path = $"logs/{new string('a', 256)}.txt";
            TelemetryOptions telemetry = new(File: new FileSinkOptions { Enabled = true, Path = path });

            validator.ValidateFileSinkPath(ConfigWith(telemetry: telemetry));
        }

        [DataTestMethod]
        [DataRow("logs/", DisplayName = "Path ending in a directory has no file name")]
        [DataRow("logs/bad\0name.txt", DisplayName = "File name contains a null character")]
        public void ValidateFileSinkPath_InvalidFileName_Throws(string path)
        {
            RuntimeConfigValidator validator = CreateValidator();
            TelemetryOptions telemetry = new(File: new FileSinkOptions { Enabled = true, Path = path });

            Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateFileSinkPath(ConfigWith(telemetry: telemetry)));
        }

        #endregion

        #region ValidateEmbeddingsOptions

        [TestMethod]
        public void ValidateEmbeddings_NotConfigured_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            validator.ValidateEmbeddingsOptions(ConfigWith(embeddings: null));
        }

        [TestMethod]
        public void ValidateEmbeddings_Disabled_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            EmbeddingsOptions embeddings = new(Provider: EmbeddingProviderType.OpenAI, BaseUrl: "https://api.openai.com", ApiKey: "key", Enabled: false);

            validator.ValidateEmbeddingsOptions(ConfigWith(embeddings: embeddings));
        }

        [TestMethod]
        public void ValidateEmbeddings_EmptyBaseUrl_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();
            EmbeddingsOptions embeddings = new(Provider: EmbeddingProviderType.OpenAI, BaseUrl: "", ApiKey: "key", Enabled: true);

            Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateEmbeddingsOptions(ConfigWith(embeddings: embeddings)));
        }

        [TestMethod]
        public void ValidateEmbeddings_InvalidBaseUrl_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();
            EmbeddingsOptions embeddings = new(Provider: EmbeddingProviderType.OpenAI, BaseUrl: "not-a-url", ApiKey: "key", Enabled: true);

            Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateEmbeddingsOptions(ConfigWith(embeddings: embeddings)));
        }

        [TestMethod]
        public void ValidateEmbeddings_EmptyApiKey_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();
            EmbeddingsOptions embeddings = new(Provider: EmbeddingProviderType.OpenAI, BaseUrl: "https://api.openai.com", ApiKey: "", Enabled: true);

            Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateEmbeddingsOptions(ConfigWith(embeddings: embeddings)));
        }

        [TestMethod]
        public void ValidateEmbeddings_AzureOpenAIWithoutModel_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();
            EmbeddingsOptions embeddings = new(Provider: EmbeddingProviderType.AzureOpenAI, BaseUrl: "https://x.openai.azure.com", ApiKey: "key", Enabled: true, Model: null);

            Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateEmbeddingsOptions(ConfigWith(embeddings: embeddings)));
        }

        [TestMethod]
        public void ValidateEmbeddings_ValidOpenAI_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            EmbeddingsOptions embeddings = new(Provider: EmbeddingProviderType.OpenAI, BaseUrl: "https://api.openai.com", ApiKey: "key", Enabled: true, Model: "text-embedding-3-small");

            validator.ValidateEmbeddingsOptions(ConfigWith(embeddings: embeddings));
        }

        [DataTestMethod]
        [DataRow(0, null, DisplayName = "Timeout must be positive")]
        [DataRow(null, 0, DisplayName = "Dimensions must be positive")]
        public void ValidateEmbeddings_NonPositiveNumericOptions_Throw(int? timeoutMs, int? dimensions)
        {
            RuntimeConfigValidator validator = CreateValidator();
            EmbeddingsOptions embeddings = new(
                Provider: EmbeddingProviderType.OpenAI,
                BaseUrl: "https://api.openai.com",
                ApiKey: "key",
                TimeoutMs: timeoutMs,
                Dimensions: dimensions);

            Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateEmbeddingsOptions(ConfigWith(embeddings: embeddings)));
        }

        [DataTestMethod]
        [DataRow(true, 0, "health check", null, DisplayName = "Enabled health threshold must be positive")]
        [DataRow(true, 1000, "", null, DisplayName = "Enabled health test text cannot be empty")]
        [DataRow(true, 1000, "health check", 0, DisplayName = "Expected dimensions must be positive")]
        public void ValidateEmbeddings_InvalidEnabledHealthOptions_Throw(
            bool enabled,
            int thresholdMs,
            string testText,
            int? expectedDimensions)
        {
            RuntimeConfigValidator validator = CreateValidator();
            EmbeddingsOptions embeddings = new(
                Provider: EmbeddingProviderType.OpenAI,
                BaseUrl: "https://api.openai.com",
                ApiKey: "key",
                Health: new EmbeddingsHealthCheckConfig(enabled, thresholdMs, testText, expectedDimensions));

            Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateEmbeddingsOptions(ConfigWith(embeddings: embeddings)));
        }

        [TestMethod]
        public void ValidateEmbeddings_EnabledEndpointWithEmptyRoles_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();
            EmbeddingsOptions embeddings = new(
                Provider: EmbeddingProviderType.OpenAI,
                BaseUrl: "https://api.openai.com",
                ApiKey: "key",
                Endpoint: new EmbeddingsEndpointOptions(enabled: true, roles: System.Array.Empty<string>()));

            Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateEmbeddingsOptions(ConfigWith(embeddings: embeddings)));
        }

        [TestMethod]
        public void ValidateEmbeddings_ProductionEndpointWithoutRoles_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();
            EmbeddingsOptions embeddings = new(
                Provider: EmbeddingProviderType.OpenAI,
                BaseUrl: "https://api.openai.com",
                ApiKey: "key",
                Endpoint: new EmbeddingsEndpointOptions(enabled: true));

            Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateEmbeddingsOptions(ConfigWith(
                    embeddings: embeddings,
                    hostMode: HostMode.Production)));
        }

        [DataTestMethod]
        [DataRow(true, 0, false, null, DisplayName = "Enabled cache lifetime must be positive")]
        [DataRow(true, 24, true, "", DisplayName = "Enabled level-two cache requires a connection string")]
        public void ValidateEmbeddings_InvalidCacheOptions_Throw(
            bool enabled,
            int ttlHours,
            bool level2Enabled,
            string? connectionString)
        {
            RuntimeConfigValidator validator = CreateValidator();
            EmbeddingsCacheOptions cache = new(
                Enabled: enabled,
                TtlHours: ttlHours,
                Level2: new EmbeddingsCacheLevel2Options(level2Enabled, connectionString));
            EmbeddingsOptions embeddings = new(
                Provider: EmbeddingProviderType.OpenAI,
                BaseUrl: "https://api.openai.com",
                ApiKey: "key",
                Cache: cache);

            Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateEmbeddingsOptions(ConfigWith(embeddings: embeddings)));
        }

        [TestMethod]
        public void ValidateEmbeddings_ValidCacheOptions_DoNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            EmbeddingsCacheOptions cache = new(
                Enabled: true,
                TtlHours: 12,
                Level2: new EmbeddingsCacheLevel2Options(true, "localhost:6379"));
            EmbeddingsOptions embeddings = new(
                Provider: EmbeddingProviderType.OpenAI,
                BaseUrl: "https://api.openai.com",
                ApiKey: "key",
                Cache: cache);

            validator.ValidateEmbeddingsOptions(ConfigWith(embeddings: embeddings));
        }

        #endregion

        #region Stored Procedure and MCP Validation

        [TestMethod]
        public void ValidateStoredProcedureDuplicateParameters_DuplicateThrows()
        {
            RuntimeConfigValidator validator = CreateValidator();
            Entity entity = CreateStoredProcedureEntity(new()
            {
                new ParameterMetadata { Name = "id" },
                new ParameterMetadata { Name = "id" }
            });

            Assert.ThrowsException<DataApiBuilderException>(() =>
                validator.ValidateStoredProcedureDuplicateParameters(ConfigWith(
                    entities: new() { ["Procedure"] = entity })));
        }

        [TestMethod]
        public void ValidateStoredProcedureDuplicateParameters_SkipsTablesAndNullParameters()
        {
            RuntimeConfigValidator validator = CreateValidator();
            Entity procedure = CreateStoredProcedureEntity(parameters: null);
            Entity table = CreateStoredProcedureEntity(new()
            {
                new ParameterMetadata { Name = "id" },
                new ParameterMetadata { Name = "id" }
            }) with
            {
                Source = new("books", EntitySourceType.Table, null, null)
            };

            validator.ValidateStoredProcedureDuplicateParameters(ConfigWith(
                entities: new() { ["Procedure"] = procedure, ["Book"] = table }));
        }

        [DataTestMethod]
        [DataRow(0, DisplayName = "Aggregate timeout cannot be zero")]
        [DataRow(601, DisplayName = "Aggregate timeout cannot exceed ten minutes")]
        public void ValidateMcpUri_OutOfRangeAggregateTimeout_Throws(int timeout)
        {
            RuntimeConfigValidator validator = CreateValidator();
            McpRuntimeOptions mcp = new(
                Enabled: true,
                Path: "/mcp",
                DmlTools: new DmlToolsConfig(aggregateRecordsQueryTimeout: timeout));

            Assert.ThrowsException<DataApiBuilderException>(() => validator.ValidateMcpUri(ConfigWith(mcp: mcp)));
        }

        [TestMethod]
        public void ValidateRelationshipConfigCorrectness_StoredProcedureRelationshipThrows()
        {
            RuntimeConfigValidator validator = CreateValidator();
            Entity entity = CreateStoredProcedureEntity(parameters: null) with
            {
                Relationships = new Dictionary<string, EntityRelationship>
                {
                    ["author"] = CreateRelationship("Author")
                }
            };

            Assert.ThrowsException<DataApiBuilderException>(() =>
                validator.ValidateRelationshipConfigCorrectness(ConfigWith(
                    entities: new() { ["Procedure"] = entity })));
        }

        [TestMethod]
        public void ValidateRelationshipConfigCorrectness_UndefinedTargetThrows()
        {
            RuntimeConfigValidator validator = CreateValidator();
            Entity entity = CreateStoredProcedureEntity(parameters: null) with
            {
                Source = new("books", EntitySourceType.Table, null, null),
                Relationships = new Dictionary<string, EntityRelationship>
                {
                    ["author"] = CreateRelationship("Missing")
                }
            };

            Assert.ThrowsException<DataApiBuilderException>(() =>
                validator.ValidateRelationshipConfigCorrectness(ConfigWith(
                    entities: new() { ["Book"] = entity })));
        }

        #endregion

        #region ValidateGlobalEndpointRouteConfig

        [TestMethod]
        public void ValidateGlobalEndpointRouteConfig_AllEndpointsDisabled_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();
            // MCP defaults to enabled when null, so it must be explicitly disabled here.
            RuntimeConfig config = ConfigWith(
                rest: new RestRuntimeOptions(Enabled: false),
                graphQL: new GraphQLRuntimeOptions(Enabled: false),
                mcp: new McpRuntimeOptions(Enabled: false, Path: "/mcp", DmlTools: null));

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateGlobalEndpointRouteConfig(config));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        [TestMethod]
        public void ValidateGlobalEndpointRouteConfig_ConflictingRestAndGraphQLPaths_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();
            RuntimeConfig config = ConfigWith(
                rest: new RestRuntimeOptions(Enabled: true, Path: "/samepath"),
                graphQL: new GraphQLRuntimeOptions(Enabled: true, Path: "/samepath"),
                mcp: null);

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateGlobalEndpointRouteConfig(config));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        [TestMethod]
        public void ValidateGlobalEndpointRouteConfig_InvalidBaseRoute_Throws()
        {
            RuntimeConfigValidator validator = CreateValidator();
            // A base route without a leading slash fails URI validation regardless of auth provider.
            RuntimeConfig config = ConfigWith(
                rest: new RestRuntimeOptions(Enabled: true, Path: "/api"),
                graphQL: new GraphQLRuntimeOptions(Enabled: true, Path: "/graphql"),
                baseRoute: "baseWithoutLeadingSlash");

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => validator.ValidateGlobalEndpointRouteConfig(config));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, ex.SubStatusCode);
        }

        [TestMethod]
        public void ValidateGlobalEndpointRouteConfig_DistinctEnabledPaths_DoesNotThrow()
        {
            RuntimeConfigValidator validator = CreateValidator();
            RuntimeConfig config = ConfigWith(
                rest: new RestRuntimeOptions(Enabled: true, Path: "/api"),
                graphQL: new GraphQLRuntimeOptions(Enabled: true, Path: "/graphql"),
                mcp: new McpRuntimeOptions(Enabled: true, Path: "/mcp", DmlTools: null));

            validator.ValidateGlobalEndpointRouteConfig(config);
        }

        #endregion

        #region Helpers

        private static RuntimeConfigValidator CreateValidator()
        {
            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);
            return new RuntimeConfigValidator(provider, fileSystem, new Mock<ILogger<RuntimeConfigValidator>>().Object);
        }

        private static RuntimeConfig ConfigWith(
            RestRuntimeOptions? rest = null,
            GraphQLRuntimeOptions? graphQL = null,
            McpRuntimeOptions? mcp = null,
            TelemetryOptions? telemetry = null,
            EmbeddingsOptions? embeddings = null,
            string? baseRoute = null,
            HostMode hostMode = HostMode.Development,
            Dictionary<string, Entity>? entities = null)
        {
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "Server=test;", Options: null),
                Runtime: new(
                    Rest: rest ?? new RestRuntimeOptions(),
                    GraphQL: graphQL ?? new GraphQLRuntimeOptions(),
                    Mcp: mcp,
                    Host: new(Cors: null, Authentication: null, Mode: hostMode),
                    BaseRoute: baseRoute,
                    Telemetry: telemetry,
                    Embeddings: embeddings),
                Entities: new(entities ?? new Dictionary<string, Entity>()));
        }

        private static Entity CreateStoredProcedureEntity(List<ParameterMetadata>? parameters)
        {
            return new Entity(
                Source: new("procedure", EntitySourceType.StoredProcedure, parameters, null),
                GraphQL: new("Procedure", "Procedures"),
                Fields: null,
                Rest: new(Enabled: true),
                Permissions: new[]
                {
                    new EntityPermission("anonymous", new[]
                    {
                        new EntityAction(EntityActionOperation.Execute, null, null)
                    })
                },
                Mappings: null,
                Relationships: null);
        }

        private static EntityRelationship CreateRelationship(string targetEntity) =>
            new(
                Cardinality.One,
                targetEntity,
                System.Array.Empty<string>(),
                System.Array.Empty<string>(),
                LinkingObject: null,
                System.Array.Empty<string>(),
                System.Array.Empty<string>());

        #endregion
    }
}
