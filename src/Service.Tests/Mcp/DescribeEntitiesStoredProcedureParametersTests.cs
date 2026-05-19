// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// When the upstream merge has copied config values onto some parameters but left others
        /// untouched, both shapes project correctly side-by-side. This single test exercises
        /// both the "override applied" branch (id) and the "no override -> defaults" branch
        /// (tenant) of <see cref="DescribeEntitiesTool.BuildParameterMetadataInfo"/>.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_MixedConfiguredAndUnconfiguredParameters_ProjectsBoth()
        {
            JsonElement parameters = await RunWithMergedDbParametersAsync(
                new Dictionary<string, ParameterDefinition>
                {
                    // id: upstream merge wrote config values onto the ParameterDefinition.
                    ["id"] = new() { Required = true, Default = "42", Description = "Config description" },
                    // tenant: not in config, so the upstream merge left ParameterDefinition untouched.
                    ["tenant"] = new()
                });

            Assert.AreEqual(2, parameters.GetArrayLength());
            AssertParameter(parameters, name: "id", expectedRequired: true, expectedDefault: "42", expectedDescription: "Config description");
            AssertParameter(parameters, name: "tenant", expectedRequired: true, expectedDefault: null, expectedDescription: string.Empty);
        }

        /// <summary>
        /// An explicit Required=false written onto the ParameterDefinition by the upstream merge
        /// is honored both when a Default value is supplied and when it is not.
        /// </summary>
        [DataTestMethod]
        [DataRow("en-US", DisplayName = "WithDefault")]
        [DataRow(null, DisplayName = "WithoutDefault")]
        public async Task DescribeEntities_ExplicitRequiredFalse_IsHonored(string expectedDefault)
        {
            JsonElement parameters = await RunWithMergedDbParametersAsync(
                new Dictionary<string, ParameterDefinition>
                {
                    ["locale"] = new() { Required = false, Default = expectedDefault, Description = "Locale override" }
                });

            Assert.AreEqual(1, parameters.GetArrayLength());
            AssertParameter(parameters, name: "locale", expectedRequired: false, expectedDefault: expectedDefault, expectedDescription: "Locale override");
        }

        // ----------------------------------------------------------------------------------
        // Invariant-violation test.
        //
        // BuildParameterMetadataInfo trusts the upstream invariant: for any successfully
        // initialized stored-procedure entity, the metadata provider has a populated
        // DatabaseStoredProcedure. SqlMetadataProvider.FillSchemaForStoredProcedureAsync
        // throws via HandleOrRecordException (aborting startup in non-validate mode) if it
        // can't populate that schema, so describe_entities never runs against a null/missing
        // SP metadata entry in production.
        //
        // If that invariant ever regresses we throw instead of fabricating empty parameter
        // info: returning an SP with parameters=[] would lie to the agent (it can't tell
        // "genuinely zero params" apart from "we don't know"). The surrounding per-entity
        // catch in DescribeEntitiesTool logs the failure and drops just that entity from the
        // response. The test below exercises that path.
        // ----------------------------------------------------------------------------------

        /// <summary>
        /// When DB metadata is missing for an SP entity, the tool drops the entity from the
        /// response rather than returning it with an empty parameters array.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_DropsEntity_WhenStoredProcedureMetadataIsMissing()
        {
            RuntimeConfig config = CreateRuntimeConfig(CreateStoredProcedureEntity());
            ServiceCollection services = new();
            RegisterCommonServices(services, config);
            // Register metadata provider with no entry for the SP entity.
            RegisterMetadataProvider(services, TEST_ENTITY_NAME, dbObject: null);

            IServiceProvider serviceProvider = services.BuildServiceProvider();
            DescribeEntitiesTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            // The only configured entity was dropped, so the tool returns the "no entities"
            // error result rather than a successful response containing a misleading entry.
            Assert.IsTrue(result.IsError == true, "Expected an error result when the only entity is dropped.");
            Assert.IsNotNull(result.Content);
            Assert.IsInstanceOfType(result.Content[0], typeof(TextContentBlock));
            string responseText = ((TextContentBlock)result.Content[0]).Text;
            StringAssert.Contains(responseText, "NoEntitiesConfigured");
        }

        /// <summary>
        /// Runs DescribeEntitiesTool against an entity whose DB metadata is already populated.
        /// The dictionary represents the state of <see cref="StoredProcedureDefinition.Parameters"/>
        /// AFTER the upstream merge in SqlMetadataProvider/MsSqlMetadataProvider has run.
        /// </summary>
        private static Task<JsonElement> RunWithMergedDbParametersAsync(
            Dictionary<string, ParameterDefinition> mergedDbParameters)
            => RunDescribeCoreAsync(dbParameters: mergedDbParameters);

        /// <summary>
        /// Sets up DI, runs DescribeEntitiesTool, and returns the parameters array of the one entity.
        /// </summary>
        private static async Task<JsonElement> RunDescribeCoreAsync(
            Dictionary<string, ParameterDefinition> dbParameters)
        {
            RuntimeConfig config = CreateRuntimeConfig(CreateStoredProcedureEntity());
            ServiceCollection services = new();
            RegisterCommonServices(services, config);

            DatabaseObject dbObject = CreateStoredProcedureObject("dbo", "get_book", dbParameters);
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
            string expectedDefault,
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

        private static Entity CreateStoredProcedureEntity()
        {
            return new Entity(
                Source: new("get_book", EntitySourceType.StoredProcedure, Parameters: null, KeyFields: null),
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
        private static void RegisterMetadataProvider(ServiceCollection services, string entityName, DatabaseObject dbObject)
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
            JsonElement root = JsonDocument.Parse(firstContent.Text).RootElement;
            JsonElement entities = root.GetProperty("entities");

            Assert.AreEqual(1, entities.GetArrayLength());
            return entities.EnumerateArray().First();
        }
    }
}
