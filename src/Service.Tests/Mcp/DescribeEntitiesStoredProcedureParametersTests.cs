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
    [TestClass]
    public class DescribeEntitiesStoredProcedureParametersTests
    {
        [TestMethod]
        public async Task DescribeEntities_IncludesStoredProcedureParametersFromMetadata_WhenConfigParametersMissing()
        {
            RuntimeConfig config = CreateRuntimeConfig(CreateStoredProcedureEntity(parameters: null));
            ServiceCollection services = new();
            RegisterCommonServices(services, config);
            RegisterMetadataProvider(services, "GetBook", CreateStoredProcedureObject("dbo", "get_book", new Dictionary<string, ParameterDefinition>
            {
                ["id"] = new() { Required = true, Description = "Book id" },
                ["locale"] = new() { Required = false, Default = "en-US", Description = "Locale" }
            }));

            IServiceProvider serviceProvider = services.BuildServiceProvider();
            DescribeEntitiesTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            Assert.IsTrue(result.IsError == false || result.IsError == null);

            JsonElement entity = GetSingleEntityFromResult(result);
            JsonElement parameters = entity.GetProperty("parameters");
            Assert.AreEqual(2, parameters.GetArrayLength(), "Stored procedure parameters should be sourced from metadata when not specified in config.");

            JsonElement idParam = parameters.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "id");
            Assert.IsTrue(idParam.GetProperty("required").GetBoolean());
            Assert.AreEqual("Book id", idParam.GetProperty("description").GetString());

            JsonElement localeParam = parameters.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "locale");
            Assert.AreEqual("en-US", localeParam.GetProperty("default").GetString());
        }

        [TestMethod]
        public async Task DescribeEntities_ConfigParameterMetadataOverridesDatabaseParameterMetadata()
        {
            List<ParameterMetadata> configuredParameters = new()
            {
                new() { Name = "id", Required = true, Default = "42", Description = "Config description" }
            };

            RuntimeConfig config = CreateRuntimeConfig(CreateStoredProcedureEntity(parameters: configuredParameters));
            ServiceCollection services = new();
            RegisterCommonServices(services, config);
            RegisterMetadataProvider(services, "GetBook", CreateStoredProcedureObject("dbo", "get_book", new Dictionary<string, ParameterDefinition>
            {
                ["id"] = new() { Required = false, Default = "1", Description = "Database description" },
                ["tenant"] = new() { Required = true, Description = "Tenant from DB" }
            }));

            IServiceProvider serviceProvider = services.BuildServiceProvider();
            DescribeEntitiesTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            Assert.IsTrue(result.IsError == false || result.IsError == null);

            JsonElement entity = GetSingleEntityFromResult(result);
            JsonElement parameters = entity.GetProperty("parameters");
            Assert.AreEqual(2, parameters.GetArrayLength());

            JsonElement idParam = parameters.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "id");
            Assert.IsFalse(idParam.GetProperty("required").GetBoolean(), "DB required metadata should be preferred when config cannot indicate whether 'required' was explicitly set.");
            Assert.AreEqual("Config description", idParam.GetProperty("description").GetString(), "Config description should override DB metadata.");
            Assert.AreEqual("42", idParam.GetProperty("default").ToString(), "Config default should override DB metadata.");

            JsonElement tenantParam = parameters.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "tenant");
            Assert.IsTrue(tenantParam.GetProperty("required").GetBoolean());
            Assert.AreEqual("Tenant from DB", tenantParam.GetProperty("description").GetString());
        }

        private static RuntimeConfig CreateRuntimeConfig(Entity storedProcedureEntity)
        {
            Dictionary<string, Entity> entities = new()
            {
                ["GetBook"] = storedProcedureEntity
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

        private static Entity CreateStoredProcedureEntity(List<ParameterMetadata> parameters)
        {
            return new Entity(
                Source: new("get_book", EntitySourceType.StoredProcedure, parameters, null),
                GraphQL: new("GetBook", "GetBook"),
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

        private static void RegisterMetadataProvider(ServiceCollection services, string entityName, DatabaseObject dbObject)
        {
            Mock<ISqlMetadataProvider> mockSqlMetadataProvider = new();
            mockSqlMetadataProvider.Setup(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>
            {
                [entityName] = dbObject
            });
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
