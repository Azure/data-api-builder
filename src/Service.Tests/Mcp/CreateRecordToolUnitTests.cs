// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    [TestClass]
    public class CreateRecordToolUnitTests
    {
        private const string ENTITY_NAME = "Book";
        private const string ARGUMENTS = "{\"entity\":\"Book\",\"data\":{\"title\":\"Dune\"}}";

        [TestMethod]
        public async Task ExecuteAsync_CanceledToken_ReturnsError()
        {
            using CancellationTokenSource cancellationTokenSource = new();
            cancellationTokenSource.Cancel();
            using JsonDocument arguments = JsonDocument.Parse(ARGUMENTS);

            CallToolResult result = await new CreateRecordTool().ExecuteAsync(
                arguments,
                CreateServiceProvider(new CreatedResult("", new { id = 1 })),
                cancellationTokenSource.Token);

            AssertErrorType(result, "Error");
        }

        [TestMethod]
        public async Task ExecuteAsync_MissingHttpContext_UsesDefaultContextAndReturnsPermissionDenied()
        {
            CallToolResult result = await ExecuteAsync(
                new CreatedResult("", new { id = 1 }),
                includeHttpContext: false);

            AssertErrorType(result, "PermissionDenied");
        }

        [TestMethod]
        public async Task ExecuteAsync_UnauthorizedOperation_ReturnsPermissionDenied()
        {
            CallToolResult result = await ExecuteAsync(
                new CreatedResult("", new { id = 1 }),
                authorizeOperation: false);

            AssertErrorType(result, "PermissionDenied");
        }

        [TestMethod]
        public async Task ExecuteAsync_UnauthorizedColumn_ReturnsPermissionDenied()
        {
            CallToolResult result = await ExecuteAsync(
                new CreatedResult("", new { id = 1 }),
                authorizeColumns: false);

            AssertErrorType(result, "PermissionDenied");
        }

        [TestMethod]
        public async Task ExecuteAsync_ColumnAuthorizationException_ReturnsValidationFailed()
        {
            DataApiBuilderException exception = new(
                "column policy failed",
                HttpStatusCode.BadRequest,
                DataApiBuilderException.SubStatusCodes.BadRequest);

            CallToolResult result = await ExecuteAsync(
                new CreatedResult("", new { id = 1 }),
                columnAuthorizationException: exception);

            AssertErrorType(result, "ValidationFailed");
        }

        [TestMethod]
        public async Task ExecuteAsync_StoredProcedure_ReturnsInvalidEntity()
        {
            DatabaseStoredProcedure storedProcedure = new("dbo", "create_book")
            {
                SourceType = EntitySourceType.StoredProcedure,
                StoredProcedureDefinition = new()
            };

            CallToolResult result = await ExecuteAsync(
                new CreatedResult("", new { id = 1 }),
                dbObject: storedProcedure);

            AssertErrorType(result, "InvalidEntity");
        }

        [TestMethod]
        public async Task ExecuteAsync_CreatedResult_ReturnsCreatedValue()
        {
            CallToolResult result = await ExecuteAsync(new CreatedResult("", new { id = 7 }));

            Assert.IsFalse(result.IsError == true);
            StringAssert.Contains(GetText(result), "\"id\": 7");
        }

        [DataTestMethod]
        [DataRow(400, true, "CreateFailed")]
        [DataRow(500, true, "CreateFailed")]
        [DataRow(403, false, "Unable to perform read-back")]
        [DataRow(200, false, "Unable to perform read-back")]
        public async Task ExecuteAsync_ObjectResult_MapsByStatus(int statusCode, bool isError, string expectedText)
        {
            ObjectResult mutationResult = new(new { detail = "result" }) { StatusCode = statusCode };

            CallToolResult result = await ExecuteAsync(mutationResult);

            Assert.AreEqual(isError, result.IsError == true);
            StringAssert.Contains(GetText(result), expectedText);
        }

        [TestMethod]
        public async Task ExecuteAsync_NullMutationResult_ReturnsUnexpectedError()
        {
            CallToolResult result = await ExecuteAsync(mutationOutcome: null);

            AssertErrorType(result, "UnexpectedError");
        }

        [TestMethod]
        public async Task ExecuteAsync_UnexpectedResultType_ReturnsSuccess()
        {
            CallToolResult result = await ExecuteAsync(new NoContentResult());

            Assert.IsFalse(result.IsError == true);
            StringAssert.Contains(GetText(result), nameof(NoContentResult));
        }

        [TestMethod]
        public async Task ExecuteAsync_MutationException_ReturnsError()
        {
            CallToolResult result = await ExecuteAsync(new InvalidOperationException("mutation failed"));

            AssertErrorType(result, "Error");
            StringAssert.Contains(GetText(result), "mutation failed");
        }

        private static async Task<CallToolResult> ExecuteAsync(
            object? mutationOutcome,
            DatabaseObject? dbObject = null,
            bool includeHttpContext = true,
            bool authorizeOperation = true,
            bool authorizeColumns = true,
            Exception? columnAuthorizationException = null)
        {
            using JsonDocument arguments = JsonDocument.Parse(ARGUMENTS);
            return await new CreateRecordTool().ExecuteAsync(
                arguments,
                CreateServiceProvider(
                    mutationOutcome,
                    dbObject,
                    includeHttpContext,
                    authorizeOperation,
                    authorizeColumns,
                    columnAuthorizationException),
                CancellationToken.None);
        }

        private static IServiceProvider CreateServiceProvider(
            object? mutationOutcome,
            DatabaseObject? dbObject = null,
            bool includeHttpContext = true,
            bool authorizeOperation = true,
            bool authorizeColumns = true,
            Exception? columnAuthorizationException = null)
        {
            RuntimeConfig config = CreateConfig();
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            ServiceCollection services = new();
            services.AddSingleton(configProvider);

            DatabaseObject resolvedObject = dbObject ?? new DatabaseView("dbo", "books_view")
            {
                SourceType = EntitySourceType.View,
                ViewDefinition = new()
            };

            Mock<ISqlMetadataProvider> metadataProvider = new();
            metadataProvider.Setup(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>
            {
                [ENTITY_NAME] = resolvedObject
            });
            metadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MSSQL);

            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            metadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(metadataProvider.Object);
            services.AddSingleton(metadataProviderFactory.Object);

            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver.Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(true);
            authorizationResolver.Setup(x => x.AreRoleAndOperationDefinedForEntity(
                ENTITY_NAME,
                AuthorizationResolver.ROLE_ANONYMOUS,
                EntityActionOperation.Create)).Returns(authorizeOperation);
            if (columnAuthorizationException is not null)
            {
                authorizationResolver.Setup(x => x.AreColumnsAllowedForOperation(
                    ENTITY_NAME,
                    AuthorizationResolver.ROLE_ANONYMOUS,
                    EntityActionOperation.Create,
                    It.IsAny<IEnumerable<string>>())).Throws(columnAuthorizationException);
            }
            else
            {
                authorizationResolver.Setup(x => x.AreColumnsAllowedForOperation(
                    ENTITY_NAME,
                    AuthorizationResolver.ROLE_ANONYMOUS,
                    EntityActionOperation.Create,
                    It.IsAny<IEnumerable<string>>())).Returns(authorizeColumns);
            }

            services.AddSingleton(authorizationResolver.Object);

            DefaultHttpContext context = new();
            context.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = AuthorizationResolver.ROLE_ANONYMOUS;
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
            {
                HttpContext = includeHttpContext ? context : null
            });

            Mock<IMutationEngine> mutationEngine = new();
            if (mutationOutcome is Exception exception)
            {
                mutationEngine.Setup(x => x.ExecuteAsync(It.IsAny<RestRequestContext>())).ThrowsAsync(exception);
            }
            else
            {
                mutationEngine.Setup(x => x.ExecuteAsync(It.IsAny<RestRequestContext>()))
                    .ReturnsAsync((IActionResult?)mutationOutcome);
            }

            Mock<IMutationEngineFactory> mutationEngineFactory = new();
            mutationEngineFactory.Setup(x => x.GetMutationEngine(DatabaseType.MSSQL)).Returns(mutationEngine.Object);
            services.AddSingleton(mutationEngineFactory.Object);
            services.AddLogging();

            return services.BuildServiceProvider();
        }

        private static RuntimeConfig CreateConfig()
        {
            Entity entity = new(
                Source: new("books", EntitySourceType.View, null, null),
                GraphQL: new("Book", "Books"),
                Fields: null,
                Rest: new(Enabled: true),
                Permissions: new[]
                {
                    new EntityPermission("anonymous", new[]
                    {
                        new EntityAction(EntityActionOperation.Create, null, null)
                    })
                },
                Mappings: null,
                Relationships: null,
                Mcp: null);

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType.MSSQL, "", null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true, Path: "/mcp", DmlTools: new(createRecord: true)),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(new Dictionary<string, Entity> { [ENTITY_NAME] = entity }));
        }

        private static string GetText(CallToolResult result)
        {
            return ((TextContentBlock)result.Content[0]).Text;
        }

        private static void AssertErrorType(CallToolResult result, string expectedType)
        {
            Assert.IsTrue(result.IsError == true, GetText(result));
            using JsonDocument document = JsonDocument.Parse(GetText(result));
            Assert.AreEqual(expectedType, document.RootElement.GetProperty("error").GetProperty("type").GetString());
        }
    }
}
