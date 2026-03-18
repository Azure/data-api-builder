// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Configuration
{
    [TestClass]
    public class McpDmlToolViewSupportTests
    {
        [DataTestMethod]
        [DataRow("CreateRecord", "Table", "{\"entity\": \"Book\", \"data\": {\"id\": 1, \"title\": \"Test\"}}", DisplayName = "CreateRecord allows Table")]
        [DataRow("CreateRecord", "View", "{\"entity\": \"BookView\", \"data\": {\"id\": 1, \"title\": \"Test\"}}", DisplayName = "CreateRecord allows View")]
        [DataRow("ReadRecords", "Table", "{\"entity\": \"Book\"}", DisplayName = "ReadRecords allows Table")]
        [DataRow("ReadRecords", "View", "{\"entity\": \"BookView\"}", DisplayName = "ReadRecords allows View")]
        [DataRow("UpdateRecord", "Table", "{\"entity\": \"Book\", \"keys\": {\"id\": 1}, \"fields\": {\"title\": \"Updated\"}}", DisplayName = "UpdateRecord allows Table")]
        [DataRow("UpdateRecord", "View", "{\"entity\": \"BookView\", \"keys\": {\"id\": 1}, \"fields\": {\"title\": \"Updated\"}}", DisplayName = "UpdateRecord allows View")]
        [DataRow("DeleteRecord", "Table", "{\"entity\": \"Book\", \"keys\": {\"id\": 1}}", DisplayName = "DeleteRecord allows Table")]
        [DataRow("DeleteRecord", "View", "{\"entity\": \"BookView\", \"keys\": {\"id\": 1}}", DisplayName = "DeleteRecord allows View")]
        public async Task DmlTool_AllowsTablesAndViews(string toolType, string sourceType, string jsonArguments)
        {
            RuntimeConfig config = sourceType == "View"
                ? CreateRuntimeConfigWithSourceType("BookView", EntitySourceType.View, "dbo.vBooks")
                : CreateRuntimeConfigWithSourceType("Book", EntitySourceType.Table, "books");

            IServiceProvider serviceProvider = CreateMcpToolServiceProvider(config);
            IMcpTool tool = CreateTool(toolType);
            JsonDocument arguments = JsonDocument.Parse(jsonArguments);

            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);
            if (result.IsError == true)
            {
                JsonElement content = ParseResultContent(result);
                if (content.TryGetProperty("error", out JsonElement error) &&
                    error.TryGetProperty("type", out JsonElement errorType))
                {
                    Assert.AreNotEqual("InvalidEntity", errorType.GetString() ?? string.Empty,
                        $"{sourceType} entities should not be blocked with InvalidEntity");
                }
            }
        }

        private static RuntimeConfig CreateRuntimeConfigWithSourceType(string entityName, EntitySourceType sourceType, string sourceObject)
        {
            Dictionary<string, Entity> entities = new()
            {
                [entityName] = new Entity(
                    Source: new EntitySource(
                        Object: sourceObject,
                        Type: sourceType,
                        Parameters: null,
                        KeyFields: new[] { "id" }
                    ),
                    GraphQL: new(entityName, entityName + "s"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[]
                    {
                        new EntityPermission(Role: "anonymous", Actions: new[]
                        {
                            new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null),
                            new EntityAction(Action: EntityActionOperation.Create, Fields: null, Policy: null),
                            new EntityAction(Action: EntityActionOperation.Update, Fields: null, Policy: null),
                            new EntityAction(Action: EntityActionOperation.Delete, Fields: null, Policy: null)
                        })
                    },
                    Mappings: null,
                    Relationships: null
                )
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(
                        Enabled: true,
                        Path: "/mcp",
                        DmlTools: new(
                            describeEntities: true,
                            readRecords: true,
                            createRecord: true,
                            updateRecord: true,
                            deleteRecord: true,
                            executeEntity: true)),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new RuntimeEntities(entities));
        }

        private static IServiceProvider CreateMcpToolServiceProvider(RuntimeConfig config)
        {
            ServiceCollection services = new();

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

            Mock<ISqlMetadataProvider> mockSqlMetadataProvider = new();
            Dictionary<string, DatabaseObject> entityToDatabaseObject = new();
            foreach (KeyValuePair<string, Entity> entry in config.Entities)
            {
                EntitySourceType mappedSourceType = entry.Value.Source.Type ?? EntitySourceType.Table;
                DatabaseObject dbObject = mappedSourceType == EntitySourceType.View
                    ? new DatabaseView("dbo", entry.Value.Source.Object) { SourceType = EntitySourceType.View }
                    : new DatabaseTable("dbo", entry.Value.Source.Object) { SourceType = EntitySourceType.Table };

                entityToDatabaseObject[entry.Key] = dbObject;
            }

            mockSqlMetadataProvider.Setup(x => x.EntityToDatabaseObject).Returns(entityToDatabaseObject);
            mockSqlMetadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MSSQL);

            Mock<IMetadataProviderFactory> mockMetadataProviderFactory = new();
            mockMetadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(mockSqlMetadataProvider.Object);
            services.AddSingleton(mockMetadataProviderFactory.Object);
            services.AddLogging();

            return services.BuildServiceProvider();
        }

        private static JsonElement ParseResultContent(CallToolResult result)
        {
            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            return JsonDocument.Parse(firstContent.Text).RootElement;
        }

        private static IMcpTool CreateTool(string toolType)
        {
            return toolType switch
            {
                "ReadRecords" => new ReadRecordsTool(),
                "CreateRecord" => new CreateRecordTool(),
                "UpdateRecord" => new UpdateRecordTool(),
                "DeleteRecord" => new DeleteRecordTool(),
                _ => throw new ArgumentException($"Unknown tool type: {toolType}", nameof(toolType))
            };
        }
    }
}
