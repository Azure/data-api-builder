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
    /// Validates the stored-procedure parameter merge contract used by
    /// <see cref="DescribeEntitiesTool.BuildParameterMetadataInfo"/>. See that method's XML doc for the rules.
    /// </summary>
    [TestClass]
    public class DescribeEntitiesStoredProcedureParametersTests
    {
        private const string TEST_ENTITY_NAME = "GetBook";

        /// <summary>
        /// Parameters discovered from DB metadata but absent from config are still surfaced. Their
        /// <c>required</c> flag defaults to <c>true</c>; <c>default</c> and <c>description</c> are config-only and absent.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_DiscoversParametersFromMetadata_WhenConfigParametersMissing()
        {
            JsonElement parameters = await RunDescribeAsync(
                configParameters: null,
                dbParameters: new Dictionary<string, ParameterDefinition>
                {
                    ["id"] = new() { Required = true, Description = "Book id (ignored: description is config-only)" },
                    ["locale"] = new() { Required = false, Default = "en-US", Description = "Locale (ignored)" }
                });

            Assert.AreEqual(2, parameters.GetArrayLength());
            AssertParameter(parameters, name: "id", expectedRequired: true, expectedDefault: null, expectedDescription: string.Empty);
            AssertParameter(parameters, name: "locale", expectedRequired: true, expectedDefault: null, expectedDescription: string.Empty);
        }

        /// <summary>
        /// When a parameter exists in both config and DB, config wins for <c>default</c> and <c>description</c>.
        /// <c>required</c> is derived from the presence of a config <c>default</c>: a config-supplied default makes
        /// the parameter optional. Parameters present only in DB default to <c>required: true</c> with no
        /// <c>default</c> or <c>description</c>.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ConfigOverridesDatabaseMetadata_AndDbOnlyParameterDefaultsToRequired()
        {
            JsonElement parameters = await RunDescribeAsync(
                configParameters: new List<ParameterMetadata>
                {
                    new() { Name = "id", Required = true, Default = "42", Description = "Config description" }
                },
                dbParameters: new Dictionary<string, ParameterDefinition>
                {
                    ["id"] = new() { Required = false, Default = "1", Description = "Database description" },
                    ["tenant"] = new() { Required = true, Description = "Tenant from DB (ignored)" }
                });

            Assert.AreEqual(2, parameters.GetArrayLength());
            AssertParameter(parameters, name: "id", expectedRequired: false, expectedDefault: "42", expectedDescription: "Config description");
            AssertParameter(parameters, name: "tenant", expectedRequired: true, expectedDefault: null, expectedDescription: string.Empty);
        }

        /// <summary>
        /// A config-supplied <c>default</c> makes the parameter optional in the emitted metadata, regardless of
        /// what the DB metadata reports for <c>Required</c>. This is the same contract used by
        /// <c>OpenApiDocumentor</c>, <c>RequestValidator</c> and <c>SqlExecuteQueryStructure</c>
        /// (the <c>HasConfigDefault</c> signal).
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ConfigDefaultMakesParameterOptional()
        {
            JsonElement parameters = await RunDescribeAsync(
                configParameters: new List<ParameterMetadata>
                {
                    new() { Name = "locale", Required = false, Default = "en-US", Description = "Locale override" }
                },
                dbParameters: new Dictionary<string, ParameterDefinition>
                {
                    ["locale"] = new() { Required = true, Description = "DB description (ignored)" }
                });

            Assert.AreEqual(1, parameters.GetArrayLength());
            AssertParameter(parameters, name: "locale", expectedRequired: false, expectedDefault: "en-US", expectedDescription: "Locale override");
        }

        /// <summary>
        /// A parameter listed in config without a <c>default</c> is reported as required, even when the user did
        /// not specify <c>required</c> in the JSON (which deserializes the non-nullable bool to <c>false</c>).
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ConfigParameterWithoutDefault_IsRequired_EvenWhenRequiredOmitted()
        {
            JsonElement parameters = await RunDescribeAsync(
                configParameters: new List<ParameterMetadata>
                {
                    new() { Name = "id", Required = false, Description = "Book id" }
                },
                dbParameters: new Dictionary<string, ParameterDefinition>
                {
                    ["id"] = new() { Required = true }
                });

            Assert.AreEqual(1, parameters.GetArrayLength());
            AssertParameter(parameters, name: "id", expectedRequired: true, expectedDefault: null, expectedDescription: "Book id");
        }

        /// <summary>
        /// A stored procedure with no parameters surfaces an empty <c>parameters</c> array when neither
        /// DB metadata nor config lists any.
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
        /// Defensive coverage for the empty-DB-parameters branch: when DB metadata reports no parameters
        /// but config lists some (a degenerate state the metadata provider validates against at startup),
        /// the config entries are surfaced via the fallback path so the response stays useful.
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
        /// Degraded fallback: when DB metadata is unavailable for the entity, the tool surfaces config-declared
        /// parameters as-is so the response is still useful.
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
        /// Builds the in-memory DI container, executes <see cref="DescribeEntitiesTool"/>, and returns the
        /// <c>parameters</c> array of the single emitted entity.
        /// </summary>
        /// <param name="configParameters">Config-declared parameters for the entity, or <c>null</c> for none.</param>
        /// <param name="dbParameters">DB-discovered parameters for the entity, or <c>null</c> to simulate the
        /// metadata provider not having an entry for the entity (degraded path).</param>
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
        /// Asserts that a parameter in the emitted JSON array matches the expected merge result.
        /// </summary>
        /// <param name="expectedDefault">Expected <c>default</c> value as a string, or <c>null</c> to assert that the JSON value is <c>null</c>.</param>
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
        /// Registers a mock metadata provider. When <paramref name="dbObject"/> is <c>null</c>, the provider
        /// is wired up but reports no mapping for the entity (simulates the "metadata not available" path).
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
