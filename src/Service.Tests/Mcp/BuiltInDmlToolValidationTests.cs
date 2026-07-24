// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Non-database unit tests for the built-in DML MCP tools (create/read/update/delete_record).
    /// Covers the early-return branches that execute before any database access:
    /// tool-disabled (runtime and entity level), null/invalid arguments, and metadata-resolution
    /// failures. No test server or database is required.
    /// </summary>
    [TestClass]
    public class BuiltInDmlToolValidationTests
    {
        #region Tool metadata & IsEnabled

        [TestMethod]
        public void ToolMetadata_HasExpectedNames()
        {
            Assert.AreEqual("create_record", new CreateRecordTool().GetToolMetadata().Name);
            Assert.AreEqual("read_records", new ReadRecordsTool().GetToolMetadata().Name);
            Assert.AreEqual("update_record", new UpdateRecordTool().GetToolMetadata().Name);
            Assert.AreEqual("delete_record", new DeleteRecordTool().GetToolMetadata().Name);
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void IsEnabled_ReflectsDmlToolsConfig(bool enabled)
        {
            RuntimeConfig config = CreateConfig(
                createEnabled: enabled, readEnabled: enabled, updateEnabled: enabled, deleteEnabled: enabled);

            Assert.AreEqual(enabled, new CreateRecordTool().IsEnabled(config));
            Assert.AreEqual(enabled, new ReadRecordsTool().IsEnabled(config));
            Assert.AreEqual(enabled, new UpdateRecordTool().IsEnabled(config));
            Assert.AreEqual(enabled, new DeleteRecordTool().IsEnabled(config));
        }

        #endregion

        #region Null arguments

        [TestMethod]
        public async Task CreateRecord_NullArguments_ReturnsInvalidArguments()
        {
            CallToolResult result = await ExecuteAsync(new CreateRecordTool(), CreateServiceProvider(CreateConfig()), arguments: null);
            AssertErrorType(result, "InvalidArguments");
        }

        [TestMethod]
        public async Task ReadRecords_NullArguments_ReturnsInvalidArguments()
        {
            CallToolResult result = await ExecuteAsync(new ReadRecordsTool(), CreateServiceProvider(CreateConfig()), arguments: null);
            AssertErrorType(result, "InvalidArguments");
        }

        [TestMethod]
        public async Task UpdateRecord_NullArguments_ReturnsInvalidArguments()
        {
            CallToolResult result = await ExecuteAsync(new UpdateRecordTool(), CreateServiceProvider(CreateConfig()), arguments: null);
            AssertErrorType(result, "InvalidArguments");
        }

        [TestMethod]
        public async Task DeleteRecord_NullArguments_ReturnsInvalidArguments()
        {
            CallToolResult result = await ExecuteAsync(new DeleteRecordTool(), CreateServiceProvider(CreateConfig()), arguments: null);
            AssertErrorType(result, "InvalidArguments");
        }

        #endregion

        #region Runtime-level tool disabled

        [TestMethod]
        public async Task CreateRecord_RuntimeDisabled_ReturnsToolDisabled()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig(createEnabled: false));
            CallToolResult result = await ExecuteAsync(new CreateRecordTool(), sp, "{\"entity\":\"Book\",\"data\":{\"title\":\"x\"}}");
            AssertErrorType(result, "ToolDisabled");
        }

        [TestMethod]
        public async Task ReadRecords_RuntimeDisabled_ReturnsToolDisabled()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig(readEnabled: false));
            CallToolResult result = await ExecuteAsync(new ReadRecordsTool(), sp, "{\"entity\":\"Book\"}");
            AssertErrorType(result, "ToolDisabled");
        }

        [TestMethod]
        public async Task UpdateRecord_RuntimeDisabled_ReturnsToolDisabled()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig(updateEnabled: false));
            CallToolResult result = await ExecuteAsync(new UpdateRecordTool(), sp, "{\"entity\":\"Book\",\"keys\":{\"id\":1},\"fields\":{\"title\":\"x\"}}");
            AssertErrorType(result, "ToolDisabled");
        }

        [TestMethod]
        public async Task DeleteRecord_RuntimeDisabled_ReturnsToolDisabled()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig(deleteEnabled: false));
            CallToolResult result = await ExecuteAsync(new DeleteRecordTool(), sp, "{\"entity\":\"Book\",\"keys\":{\"id\":1}}");
            AssertErrorType(result, "ToolDisabled");
        }

        #endregion

        #region Invalid arguments

        [TestMethod]
        public async Task CreateRecord_MissingData_ReturnsInvalidArguments()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig());
            CallToolResult result = await ExecuteAsync(new CreateRecordTool(), sp, "{\"entity\":\"Book\"}");
            AssertErrorType(result, "InvalidArguments");
        }

        [TestMethod]
        public async Task ReadRecords_MissingEntity_ReturnsInvalidArguments()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig());
            CallToolResult result = await ExecuteAsync(new ReadRecordsTool(), sp, "{\"select\":\"id\"}");
            AssertErrorType(result, "InvalidArguments");
        }

        [TestMethod]
        public async Task UpdateRecord_MissingFields_ReturnsInvalidArguments()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig());
            CallToolResult result = await ExecuteAsync(new UpdateRecordTool(), sp, "{\"entity\":\"Book\",\"keys\":{\"id\":1}}");
            AssertErrorType(result, "InvalidArguments");
        }

        [TestMethod]
        public async Task DeleteRecord_MissingKeys_ReturnsInvalidArguments()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig());
            CallToolResult result = await ExecuteAsync(new DeleteRecordTool(), sp, "{\"entity\":\"Book\"}");
            AssertErrorType(result, "InvalidArguments");
        }

        #endregion

        #region Entity-level DML disabled

        [TestMethod]
        public async Task CreateRecord_EntityDmlDisabled_ReturnsToolDisabled()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig(entityDmlEnabled: false));
            CallToolResult result = await ExecuteAsync(new CreateRecordTool(), sp, "{\"entity\":\"Book\",\"data\":{\"title\":\"x\"}}");
            AssertErrorType(result, "ToolDisabled");
        }

        [TestMethod]
        public async Task DeleteRecord_EntityDmlDisabled_ReturnsToolDisabled()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig(entityDmlEnabled: false));
            CallToolResult result = await ExecuteAsync(new DeleteRecordTool(), sp, "{\"entity\":\"Book\",\"keys\":{\"id\":1}}");
            AssertErrorType(result, "ToolDisabled");
        }

        [TestMethod]
        public async Task UpdateRecord_EntityDmlDisabled_ReturnsToolDisabled()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig(entityDmlEnabled: false));
            CallToolResult result = await ExecuteAsync(new UpdateRecordTool(), sp, "{\"entity\":\"Book\",\"keys\":{\"id\":1},\"fields\":{\"title\":\"x\"}}");
            AssertErrorType(result, "ToolDisabled");
        }

        #endregion

        #region Metadata resolution failure (no metadata provider registered)

        [TestMethod]
        public async Task CreateRecord_UnresolvableMetadata_ReturnsError()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig());
            CallToolResult result = await ExecuteAsync(new CreateRecordTool(), sp, "{\"entity\":\"Book\",\"data\":{\"title\":\"x\"}}");
            Assert.IsTrue(result.IsError == true);
        }

        [TestMethod]
        public async Task ReadRecords_UnresolvableMetadata_ReturnsError()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig());
            CallToolResult result = await ExecuteAsync(new ReadRecordsTool(), sp, "{\"entity\":\"Book\"}");
            Assert.IsTrue(result.IsError == true);
        }

        [TestMethod]
        public async Task DeleteRecord_UnresolvableMetadata_ReturnsError()
        {
            IServiceProvider sp = CreateServiceProvider(CreateConfig());
            CallToolResult result = await ExecuteAsync(new DeleteRecordTool(), sp, "{\"entity\":\"Book\",\"keys\":{\"id\":1}}");
            Assert.IsTrue(result.IsError == true);
        }

        #endregion

        #region Helpers

        private static async Task<CallToolResult> ExecuteAsync(IMcpTool tool, IServiceProvider sp, string? arguments)
        {
            if (arguments is null)
            {
                return await tool.ExecuteAsync(null, sp, CancellationToken.None);
            }

            using JsonDocument args = JsonDocument.Parse(arguments);
            return await tool.ExecuteAsync(args, sp, CancellationToken.None);
        }

        private static void AssertErrorType(CallToolResult result, string expectedType)
        {
            Assert.IsTrue(result.IsError == true, "Expected an error result.");
            TextContentBlock block = (TextContentBlock)result.Content[0];
            JsonElement root = JsonDocument.Parse(block.Text).RootElement;
            Assert.AreEqual(expectedType, root.GetProperty("error").GetProperty("type").GetString());
        }

        private static RuntimeConfig CreateConfig(
            bool createEnabled = true,
            bool readEnabled = true,
            bool updateEnabled = true,
            bool deleteEnabled = true,
            bool entityDmlEnabled = true)
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    GraphQL: new("Book", "Books"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[]
                    {
                        new EntityPermission(Role: "anonymous", Actions: new[]
                        {
                            new EntityAction(Action: EntityActionOperation.All, Fields: null, Policy: null)
                        })
                    },
                    Mappings: null,
                    Relationships: null,
                    Mcp: entityDmlEnabled ? null : new EntityMcpOptions(customToolEnabled: false, dmlToolsEnabled: false))
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
                            readRecords: readEnabled,
                            createRecord: createEnabled,
                            updateRecord: updateEnabled,
                            deleteRecord: deleteEnabled,
                            executeEntity: true,
                            aggregateRecords: true)),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(entities));
        }

        private static IServiceProvider CreateServiceProvider(RuntimeConfig config)
        {
            ServiceCollection services = new();

            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            services.AddSingleton(configProvider);

            Mock<IAuthorizationResolver> authResolver = new();
            authResolver.Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(true);
            services.AddSingleton(authResolver.Object);

            Mock<HttpContext> httpContext = new();
            Mock<HttpRequest> request = new();
            request.Setup(x => x.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns("anonymous");
            httpContext.Setup(x => x.Request).Returns(request.Object);

            Mock<IHttpContextAccessor> httpContextAccessor = new();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext.Object);
            services.AddSingleton(httpContextAccessor.Object);

            services.AddLogging();

            return services.BuildServiceProvider();
        }

        #endregion
    }
}
