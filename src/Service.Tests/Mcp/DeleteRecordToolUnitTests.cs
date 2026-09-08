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
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    [TestClass]
    public class DeleteRecordToolUnitTests
    {
        private const string ENTITY_NAME = "Book";

        [TestMethod]
        public async Task ExecuteAsync_CanceledToken_ReturnsOperationCanceled()
        {
            using CancellationTokenSource cancellationTokenSource = new();
            cancellationTokenSource.Cancel();

            CallToolResult result = await new DeleteRecordTool().ExecuteAsync(
                arguments: null,
                CreateServiceProvider(),
                cancellationTokenSource.Token);

            AssertErrorType(result, "OperationCanceled");
        }

        [TestMethod]
        public async Task ExecuteAsync_NullKey_ReturnsInvalidArguments()
        {
            CallToolResult result = await ExecuteAsync("{\"entity\":\"Book\",\"keys\":{\"id\":null}}", new NoContentResult());

            AssertErrorType(result, "InvalidArguments");
            StringAssert.Contains(GetText(result), "cannot be null");
        }

        [TestMethod]
        public async Task ExecuteAsync_StoredProcedure_ReturnsInvalidEntity()
        {
            DatabaseStoredProcedure storedProcedure = new("dbo", "get_book")
            {
                SourceType = EntitySourceType.StoredProcedure,
                StoredProcedureDefinition = new()
            };

            CallToolResult result = await ExecuteAsync(
                "{\"entity\":\"Book\",\"keys\":{\"id\":1}}",
                new NoContentResult(),
                dbObject: storedProcedure);

            AssertErrorType(result, "InvalidEntity");
        }

        [TestMethod]
        public async Task ExecuteAsync_MissingHttpContext_ReturnsPermissionDenied()
        {
            CallToolResult result = await ExecuteAsync(
                "{\"entity\":\"Book\",\"keys\":{\"id\":1}}",
                new NoContentResult(),
                includeHttpContext: false);

            AssertErrorType(result, "PermissionDenied");
        }

        [TestMethod]
        public async Task ExecuteAsync_UnauthorizedOperation_ReturnsPermissionDenied()
        {
            CallToolResult result = await ExecuteAsync(
                "{\"entity\":\"Book\",\"keys\":{\"id\":1}}",
                new NoContentResult(),
                authorizeOperation: false);

            AssertErrorType(result, "PermissionDenied");
        }

        [TestMethod]
        public async Task ExecuteAsync_NoContentResult_ReturnsSuccess()
        {
            CallToolResult result = await ExecuteAsync("{\"entity\":\"Book\",\"keys\":{\"id\":1}}", new NoContentResult());

            Assert.IsFalse(result.IsError == true);
            StringAssert.Contains(GetText(result), "Record deleted successfully");
            StringAssert.Contains(GetText(result), "id=1");
        }

        [TestMethod]
        public async Task ExecuteAsync_OkObjectResult_IncludesResult()
        {
            CallToolResult result = await ExecuteAsync(
                "{\"entity\":\"Book\",\"keys\":{\"id\":1}}",
                new OkObjectResult(new { deleted = 1 }));

            Assert.IsFalse(result.IsError == true);
            StringAssert.Contains(GetText(result), "deleted");
        }

        /// <summary>
        /// Verifies the MCP error type inferred from recognizable messages in DAB request failures, including the generic fallback.
        /// </summary>
        [DataTestMethod]
        [DataRow("Could not find item with id", "RecordNotFound")]
        [DataRow("violates foreign key constraint", "ConstraintViolation")]
        [DataRow("REFERENCE constraint failure", "ConstraintViolation")]
        [DataRow("authorization failed", "PermissionDenied")]
        [DataRow("invalid key type", "InvalidArguments")]
        [DataRow("other DAB failure", "DataApiBuilderError")]
        public async Task ExecuteAsync_DataApiBuilderException_MapsError(string message, string expectedError)
        {
            DataApiBuilderException exception = new(
                message,
                HttpStatusCode.BadRequest,
                DataApiBuilderException.SubStatusCodes.BadRequest);

            CallToolResult result = await ExecuteAsync("{\"entity\":\"Book\",\"keys\":{\"id\":1}}", exception);

            AssertErrorType(result, expectedError);
        }

        /// <summary>
        /// Verifies the MCP error type inferred from provider exception messages, including the generic database fallback.
        /// </summary>
        [DataTestMethod]
        [DataRow("foreign key failure", "ConstraintViolation")]
        [DataRow("record does not exist", "RecordNotFound")]
        [DataRow("provider exploded", "DatabaseError")]
        public async Task ExecuteAsync_DbException_MapsError(string message, string expectedError)
        {
            CallToolResult result = await ExecuteAsync(
                "{\"entity\":\"Book\",\"keys\":{\"id\":1}}",
                new FakeDbException(message));

            AssertErrorType(result, expectedError);
        }

        /// <summary>
        /// Verifies the MCP error type inferred from non-provider exception messages, including the unexpected-error fallback.
        /// </summary>
        [DataTestMethod]
        [DataRow("connection unavailable", "ConnectionError")]
        [DataRow("Could not find record", "RecordNotFound")]
        [DataRow("unexpected failure", "UnexpectedError")]
        public async Task ExecuteAsync_GeneralException_MapsError(string message, string expectedError)
        {
            CallToolResult result = await ExecuteAsync(
                "{\"entity\":\"Book\",\"keys\":{\"id\":1}}",
                new InvalidOperationException(message));

            AssertErrorType(result, expectedError);
        }

        [TestMethod]
        public async Task ExecuteAsync_Timeout_ReturnsTimeoutError()
        {
            CallToolResult result = await ExecuteAsync(
                "{\"entity\":\"Book\",\"keys\":{\"id\":1}}",
                new TimeoutException());

            AssertErrorType(result, "TimeoutError");
        }

        /// <summary>
        /// Verifies the user-facing message selected for known SQL Server error numbers and the unknown-number fallback.
        /// </summary>
        [DataTestMethod]
        [DataRow(547, "foreign key constraint")]
        [DataRow(2627, "unique constraint")]
        [DataRow(2601, "unique constraint")]
        [DataRow(229, "Permission denied")]
        [DataRow(262, "Permission denied")]
        [DataRow(208, "not found")]
        [DataRow(50000, "Database error")]
        public async Task ExecuteAsync_SqlException_MapsErrorNumber(int errorNumber, string expectedMessage)
        {
            CallToolResult result = await ExecuteAsync(
                "{\"entity\":\"Book\",\"keys\":{\"id\":1}}",
                SqlTestHelper.CreateSqlException(errorNumber, "provider error"));

            AssertErrorType(result, "DatabaseError");
            StringAssert.Contains(GetText(result), expectedMessage);
        }

        [TestMethod]
        public async Task ExecuteAsync_InvalidPrimaryKey_ReturnsDataApiBuilderError()
        {
            CallToolResult result = await ExecuteAsync(
                "{\"entity\":\"Book\",\"keys\":{\"other\":1}}",
                new NoContentResult());

            AssertErrorType(result, "UnexpectedError");
        }

        private static async Task<CallToolResult> ExecuteAsync(
            string arguments,
            object mutationOutcome,
            DatabaseObject? dbObject = null,
            bool includeHttpContext = true,
            bool authorizeOperation = true)
        {
            using JsonDocument document = JsonDocument.Parse(arguments);
            IServiceProvider serviceProvider = CreateServiceProvider(
                mutationOutcome,
                dbObject,
                includeHttpContext,
                authorizeOperation);
            return await new DeleteRecordTool().ExecuteAsync(document, serviceProvider, CancellationToken.None);
        }

        private static IServiceProvider CreateServiceProvider(
            object? mutationOutcome = null,
            DatabaseObject? dbObject = null,
            bool includeHttpContext = true,
            bool authorizeOperation = true)
        {
            RuntimeConfig config = CreateConfig();
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            ServiceCollection services = new();
            services.AddSingleton(configProvider);

            SourceDefinition sourceDefinition = new() { PrimaryKey = new() { "id" } };
            DatabaseObject resolvedObject = dbObject ?? new DatabaseTable("dbo", "books")
            {
                SourceType = EntitySourceType.Table,
                TableDefinition = sourceDefinition
            };

            Mock<ISqlMetadataProvider> metadataProvider = new();
            metadataProvider.Setup(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>
            {
                [ENTITY_NAME] = resolvedObject
            });
            metadataProvider.Setup(x => x.GetSourceDefinition(ENTITY_NAME)).Returns(sourceDefinition);
            string? idBackingColumn = "id";
            metadataProvider.Setup(x => x.TryGetBackingColumn(ENTITY_NAME, "id", out idBackingColumn)).Returns(true);
            string? missingBackingColumn = null;
            metadataProvider.Setup(x => x.TryGetBackingColumn(ENTITY_NAME, "other", out missingBackingColumn)).Returns(false);

            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            metadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(metadataProvider.Object);
            services.AddSingleton(metadataProviderFactory.Object);

            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver.Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(true);
            authorizationResolver.Setup(x => x.AreRoleAndOperationDefinedForEntity(
                ENTITY_NAME,
                AuthorizationResolver.ROLE_ANONYMOUS,
                EntityActionOperation.Delete)).Returns(authorizeOperation);
            services.AddSingleton(authorizationResolver.Object);

            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = AuthorizationResolver.ROLE_ANONYMOUS;
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
            {
                HttpContext = includeHttpContext ? httpContext : null
            });

            Mock<IMutationEngine> mutationEngine = new();
            if (mutationOutcome is Exception exception)
            {
                mutationEngine.Setup(x => x.ExecuteAsync(It.IsAny<RestRequestContext>())).ThrowsAsync(exception);
            }
            else
            {
                mutationEngine.Setup(x => x.ExecuteAsync(It.IsAny<RestRequestContext>()))
                    .ReturnsAsync((IActionResult?)mutationOutcome ?? new NoContentResult());
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
                Source: new("books", EntitySourceType.Table, null, null),
                GraphQL: new("Book", "Books"),
                Fields: null,
                Rest: new(Enabled: true),
                Permissions: new[]
                {
                    new EntityPermission("anonymous", new[]
                    {
                        new EntityAction(EntityActionOperation.Delete, null, null)
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
                    Mcp: new(Enabled: true, Path: "/mcp", DmlTools: new(deleteRecord: true)),
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
