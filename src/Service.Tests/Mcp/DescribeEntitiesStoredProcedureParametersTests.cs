// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Tests how DescribeEntitiesTool merges stored-procedure parameter info from the DB and the config.
    /// See <see cref="DescribeEntitiesTool.BuildParameterMetadataInfo"/> for the rules.
    /// </summary>
    [TestClass]
    public class DescribeEntitiesStoredProcedureParametersTests
    {
        private const string TEST_ENTITY_NAME = "GetBook";

        /// <summary>
        /// Parameters not declared in config are still surfaced from DB metadata.
        /// Per rules 2/3/4/5 of issue #3400, required defaults to true and default/description
        /// stay null/empty because they are config-only and the upstream merge wrote nothing.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_UnconfiguredParameters_DefaultToRequiredWithNoDefaultOrDescription()
        {
            JsonElement parameters = await RunDescribeAsync(
                configParameters: null,
                dbParameters: new Dictionary<string, ParameterDefinition>
                {
                    ["id"] = new(),
                    ["locale"] = new()
                });

            Assert.AreEqual(2, parameters.GetArrayLength());
            AssertParameter(parameters, name: "id", expectedRequired: true, expectedDefault: null, expectedDescription: string.Empty);
            AssertParameter(parameters, name: "locale", expectedRequired: true, expectedDefault: null, expectedDescription: string.Empty);
        }

        /// <summary>
        /// Config values supplied per-parameter (merged upstream by SqlMetadataProvider) flow through.
        /// Parameters absent from config still appear with the default required=true.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_MixedConfiguredAndUnconfiguredParameters_ProjectsBoth()
        {
            JsonElement parameters = await RunDescribeAsync(
                configParameters: new List<ParameterMetadata>
                {
                    new() { Name = "id", Required = true, Default = "42", Description = "Config description" }
                },
                dbParameters: new Dictionary<string, ParameterDefinition>
                {
                    ["id"] = new() { Required = true, Default = "42", Description = "Config description" },
                    ["tenant"] = new()
                });

            Assert.AreEqual(2, parameters.GetArrayLength());
            AssertParameter(parameters, name: "id", expectedRequired: true, expectedDefault: "42", expectedDescription: "Config description");
            AssertParameter(parameters, name: "tenant", expectedRequired: true, expectedDefault: null, expectedDescription: string.Empty);
        }

        /// <summary>
        /// An explicit Required=false from config (already merged onto the ParameterDefinition) is honored.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ExplicitRequiredFalse_IsHonored()
        {
            JsonElement parameters = await RunDescribeAsync(
                configParameters: new List<ParameterMetadata>
                {
                    new() { Name = "locale", Required = false, Default = "en-US", Description = "Locale override" }
                },
                dbParameters: new Dictionary<string, ParameterDefinition>
                {
                    ["locale"] = new() { Required = false, Default = "en-US", Description = "Locale override" }
                });

            Assert.AreEqual(1, parameters.GetArrayLength());
            AssertParameter(parameters, name: "locale", expectedRequired: false, expectedDefault: "en-US", expectedDescription: "Locale override");
        }

        /// <summary>
        /// Explicit Required=false is honored even when there is no Default value supplied.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ExplicitRequiredFalse_IsHonored_WhenNoDefault()
        {
            JsonElement parameters = await RunDescribeAsync(
                configParameters: new List<ParameterMetadata>
                {
                    new() { Name = "id", Required = false, Description = "Book id" }
                },
                dbParameters: new Dictionary<string, ParameterDefinition>
                {
                    ["id"] = new() { Required = false, Description = "Book id" }
                });

            Assert.AreEqual(1, parameters.GetArrayLength());
            AssertParameter(parameters, name: "id", expectedRequired: false, expectedDefault: null, expectedDescription: "Book id");
        }

        /// <summary>
        /// A stored procedure with no parameters returns an empty parameters array.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_StoredProcedureWithNoParameters_EmitsEmptyParametersArray()
        {
            JsonElement parameters = await RunDescribeAsync(
                configParameters: null,
                dbParameters: new Dictionary<string, ParameterDefinition>());

            Assert.AreEqual(JsonValueKind.Array, parameters.ValueKind);
            Assert.AreEqual(0, parameters.GetArrayLength());
        }

        /// <summary>
        /// When the DB reports no parameters, the tool falls back to the config parameters.
        /// Per rule 2 of issue #3400, a config entry without an explicit Required value
        /// still defaults to required=true.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_EmptyDbParameters_FallsBackToConfigParameters()
        {
            JsonElement parameters = await RunDescribeAsync(
                configParameters: new List<ParameterMetadata>
                {
                    new() { Name = "id", Description = "Book id" }
                },
                dbParameters: new Dictionary<string, ParameterDefinition>());

            Assert.AreEqual(1, parameters.GetArrayLength());
            AssertParameter(parameters, name: "id", expectedRequired: true, expectedDefault: null, expectedDescription: "Book id");
        }

        /// <summary>
        /// When DB metadata is not available, the tool falls back to the config parameters.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_FallsBackToConfigParameters_WhenDatabaseMetadataUnavailable()
        {
            JsonElement parameters = await RunDescribeAsync(
                configParameters: new List<ParameterMetadata>
                {
                    new() { Name = "id", Required = true, Description = "Book id" },
                    new() { Name = "locale", Required = false, Default = "en-US", Description = "Locale" }
                },
                dbParameters: null);

            Assert.AreEqual(2, parameters.GetArrayLength());
            AssertParameter(parameters, name: "id", expectedRequired: true, expectedDefault: null, expectedDescription: "Book id");
            AssertParameter(parameters, name: "locale", expectedRequired: false, expectedDefault: "en-US", expectedDescription: "Locale");
        }

        /// <summary>
        /// Sets up DI, runs DescribeEntitiesTool, and returns the parameters array of the one entity.
        /// </summary>
        /// <param name="configParameters">Parameters listed in config, or null for none.</param>
        /// <param name="dbParameters">Parameters reported by DB metadata, or null to simulate the
        /// metadata provider not knowing about the entity.</param>
        private static async Task<JsonElement> RunDescribeAsync(
            List<ParameterMetadata>? configParameters,
            Dictionary<string, ParameterDefinition>? dbParameters)
        {
            RuntimeConfig config = CreateRuntimeConfig(CreateStoredProcedureEntity(parameters: configParameters));
            ServiceCollection services = new();
            RegisterCommonServices(services, config);

            DatabaseObject? dbObject = dbParameters is null
                ? null
                : CreateStoredProcedureObject("dbo", "get_book", dbParameters);
            RegisterMetadataProvider(services, TEST_ENTITY_NAME, dbObject);

            IServiceProvider serviceProvider = services.BuildServiceProvider();
            DescribeEntitiesTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);
            Assert.IsTrue(result.IsError == false || result.IsError == null);

            JsonElement entity = GetSingleEntityFromResult(result);
            return entity.GetProperty("parameters");
        }

        /// <summary>
        /// Checks that one parameter in the JSON array has the expected required, default, and description.
        /// </summary>
        /// <param name="expectedDefault">Expected default value, or null to assert the JSON value is null.</param>
        private static void AssertParameter(
            JsonElement parameters,
            string name,
            bool expectedRequired,
            string? expectedDefault,
            string expectedDescription)
        {
            JsonElement param = parameters.EnumerateArray().Single(p => p.GetProperty("name").GetString() == name);

            Assert.AreEqual(expectedRequired, param.GetProperty("required").GetBoolean(), $"required mismatch for parameter '{name}'.");

            JsonElement defaultElement = param.GetProperty("default");
            if (expectedDefault is null)
            {
                Assert.AreEqual(JsonValueKind.Null, defaultElement.ValueKind, $"default should be JSON null for parameter '{name}'.");
            }
            else
            {
                Assert.AreEqual(expectedDefault, defaultElement.GetString(), $"default mismatch for parameter '{name}'.");
            }

            Assert.AreEqual(expectedDescription, param.GetProperty("description").GetString(), $"description mismatch for parameter '{name}'.");
        }

        private static RuntimeConfig CreateRuntimeConfig(Entity storedProcedureEntity)
        {
            Dictionary<string, Entity> entities = new()
            {
                [TEST_ENTITY_NAME] = storedProcedureEntity
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true, Path: "/mcp", DmlTools: null),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities)
            );
        }

        private static Entity CreateStoredProcedureEntity(List<ParameterMetadata>? parameters)
        {
            return new Entity(
                Source: new("get_book", EntitySourceType.StoredProcedure, parameters, null),
                GraphQL: new(TEST_ENTITY_NAME, TEST_ENTITY_NAME),
                Rest: new(Enabled: true),
                Fields: null,
                Permissions: new[]
                {
                    new EntityPermission(
                        Role: "anonymous",
                        Actions: new[]
                        {
                            new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null)
                        })
                },
                Relationships: null,
                Mappings: null,
                Mcp: null
            );
        }

        private static DatabaseStoredProcedure CreateStoredProcedureObject(
            string schema,
            string name,
            Dictionary<string, ParameterDefinition> parameters)
        {
            return new DatabaseStoredProcedure(schema, name)
            {
                SourceType = EntitySourceType.StoredProcedure,
                StoredProcedureDefinition = new StoredProcedureDefinition
                {
                    Parameters = parameters
                }
            };
        }

        private static void RegisterCommonServices(ServiceCollection services, RuntimeConfig config)
        {
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            services.AddSingleton(configProvider);

            Mock<IAuthorizationResolver> mockAuthResolver = new();
            mockAuthResolver.Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(true);
            services.AddSingleton(mockAuthResolver.Object);

            Mock<HttpContext> mockHttpContext = new();
            Mock<HttpRequest> mockRequest = new();
            mockRequest.Setup(x => x.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns("anonymous");
            mockHttpContext.Setup(x => x.Request).Returns(mockRequest.Object);

            Mock<IHttpContextAccessor> mockHttpContextAccessor = new();
            mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);
            services.AddSingleton(mockHttpContextAccessor.Object);

            services.AddLogging();
        }

        /// <summary>
        /// Registers a mock metadata provider. If <paramref name="dbObject"/> is null, the provider has
        /// no entry for the entity (simulates DB metadata not available).
        /// </summary>
        private static void RegisterMetadataProvider(ServiceCollection services, string entityName, DatabaseObject? dbObject)
        {
            Dictionary<string, DatabaseObject> entityMap = dbObject is null
                ? new Dictionary<string, DatabaseObject>()
                : new Dictionary<string, DatabaseObject> { [entityName] = dbObject };

            Mock<ISqlMetadataProvider> mockSqlMetadataProvider = new();
            mockSqlMetadataProvider.Setup(x => x.EntityToDatabaseObject).Returns(entityMap);
            mockSqlMetadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MSSQL);

            Mock<IMetadataProviderFactory> mockMetadataProviderFactory = new();
            mockMetadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(mockSqlMetadataProvider.Object);
            services.AddSingleton(mockMetadataProviderFactory.Object);
        }

        private static JsonElement GetSingleEntityFromResult(CallToolResult result)
        {
            Assert.IsNotNull(result.Content);
            Assert.IsTrue(result.Content.Count > 0);
            Assert.IsInstanceOfType(result.Content[0], typeof(TextContentBlock));

            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            JsonElement root = JsonDocument.Parse(firstContent.Text!).RootElement;
            JsonElement entities = root.GetProperty("entities");

            Assert.AreEqual(1, entities.GetArrayLength());
            return entities.EnumerateArray().First();
        }
    }
}
