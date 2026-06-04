// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.Cache;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Shared base class for MCP tool integration tests.
    /// Provides common service provider builders and assertion helpers to avoid duplication.
    /// </summary>
    public abstract class McpToolTestBase : SqlTestBase
    {
        #region Service Provider Builders

        /// <summary>
        /// Builds a service provider for read-only tools (ReadRecordsTool, AggregateRecordsTool).
        /// Includes: RuntimeConfigProvider, IMetadataProviderFactory, IAuthorizationResolver,
        /// IAuthorizationService, IHttpContextAccessor, IQueryEngineFactory, GQLFilterParser,
        /// IAbstractQueryManagerFactory.
        /// </summary>
        protected static IServiceProvider BuildQueryServiceProvider()
        {
            ServiceCollection services = new();

            RuntimeConfigProvider configProvider = _application.Services.GetRequiredService<RuntimeConfigProvider>();
            services.AddSingleton(configProvider);

            services.AddSingleton(_metadataProviderFactory.Object);
            services.AddSingleton(_authorizationResolver);
            services.AddSingleton(_gqlFilterParser);
            services.AddSingleton(_queryManagerFactory.Object);

            IHttpContextAccessor httpContextAccessor = CreateAnonymousHttpContextAccessor();
            services.AddSingleton(httpContextAccessor);

            Mock<IAuthorizationService> authorizationService = new();
            authorizationService
                .Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
                .ReturnsAsync(AuthorizationResult.Success());
            services.AddSingleton(authorizationService.Object);

            Mock<IFusionCache> cache = new();
            DabCacheService cacheService = new(cache.Object, logger: null, httpContextAccessor);

            SqlQueryEngine queryEngine = new(
                _queryManagerFactory.Object,
                _metadataProviderFactory.Object,
                httpContextAccessor,
                _authorizationResolver,
                _gqlFilterParser,
                new Mock<ILogger<IQueryEngine>>().Object,
                configProvider,
                cacheService);

            Mock<IQueryEngineFactory> queryEngineFactory = new();
            queryEngineFactory
                .Setup(f => f.GetQueryEngine(It.IsAny<DatabaseType>()))
                .Returns(queryEngine);
            services.AddSingleton(queryEngineFactory.Object);

            services.AddLogging();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Builds a service provider for mutation tools (CreateRecordTool, UpdateRecordTool, DeleteRecordTool).
        /// Includes: RuntimeConfigProvider, IMetadataProviderFactory, IAuthorizationResolver,
        /// IHttpContextAccessor, IMutationEngineFactory, RequestValidator.
        /// </summary>
        protected static IServiceProvider BuildMutationServiceProvider()
        {
            ServiceCollection services = new();

            RuntimeConfigProvider configProvider = _application.Services.GetRequiredService<RuntimeConfigProvider>();
            services.AddSingleton(configProvider);

            services.AddSingleton(_metadataProviderFactory.Object);
            services.AddSingleton(_authorizationResolver);

            IHttpContextAccessor httpContextAccessor = CreateAnonymousHttpContextAccessor();
            services.AddSingleton(httpContextAccessor);

            services.AddSingleton(new RequestValidator(_metadataProviderFactory.Object, configProvider));

            Mock<IFusionCache> cache = new();
            DabCacheService cacheService = new(cache.Object, logger: null, httpContextAccessor);

            SqlQueryEngine queryEngine = new(
                _queryManagerFactory.Object,
                _metadataProviderFactory.Object,
                httpContextAccessor,
                _authorizationResolver,
                _gqlFilterParser,
                new Mock<ILogger<IQueryEngine>>().Object,
                configProvider,
                cacheService);

            Mock<IQueryEngineFactory> queryEngineFactory = new();
            queryEngineFactory
                .Setup(f => f.GetQueryEngine(It.IsAny<DatabaseType>()))
                .Returns(queryEngine);

            SqlMutationEngine mutationEngine = new(
                _queryManagerFactory.Object,
                _metadataProviderFactory.Object,
                queryEngineFactory.Object,
                _authorizationResolver,
                _gqlFilterParser,
                httpContextAccessor,
                configProvider);

            Mock<IMutationEngineFactory> mutationEngineFactory = new();
            mutationEngineFactory
                .Setup(f => f.GetMutationEngine(It.IsAny<DatabaseType>()))
                .Returns(mutationEngine);
            services.AddSingleton(mutationEngineFactory.Object);

            services.AddLogging();

            return services.BuildServiceProvider();
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Creates an HttpContextAccessor with anonymous role claims for MCP tool testing.
        /// </summary>
        protected static IHttpContextAccessor CreateAnonymousHttpContextAccessor()
        {
            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = AuthorizationResolver.ROLE_ANONYMOUS;
            ClaimsIdentity identity = new(
                authenticationType: "TestAuth",
                nameType: null,
                roleType: AuthenticationOptions.ROLE_CLAIM_TYPE);
            identity.AddClaim(new Claim(AuthenticationOptions.ROLE_CLAIM_TYPE, AuthorizationResolver.ROLE_ANONYMOUS));
            httpContext.User = new ClaimsPrincipal(identity);
            return new HttpContextAccessor { HttpContext = httpContext };
        }

        /// <summary>
        /// Executes an MCP tool with the given JSON arguments and service provider.
        /// </summary>
        protected static async Task<CallToolResult> ExecuteToolAsync(IMcpTool tool, IServiceProvider serviceProvider, Dictionary<string, object?> args)
        {
            string argsJson = JsonSerializer.Serialize(args);
            using JsonDocument arguments = JsonDocument.Parse(argsJson);
            return await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);
        }

        /// <summary>
        /// Extracts the text content from the first content block of a CallToolResult.
        /// </summary>
        protected static string GetFirstTextContent(CallToolResult result)
        {
            if (result.Content is null || result.Content.Count == 0)
            {
                return string.Empty;
            }

            return result.Content[0] is TextContentBlock textBlock
                ? textBlock.Text ?? string.Empty
                : string.Empty;
        }

        /// <summary>
        /// Asserts that a CallToolResult is not an error.
        /// </summary>
        protected static void AssertSuccess(CallToolResult result, string message)
        {
            Assert.IsTrue(result.IsError != true,
                $"{message} Content: {GetFirstTextContent(result)}");
        }

        /// <summary>
        /// Asserts that a CallToolResult is an error and its content contains the expected substring.
        /// </summary>
        protected static void AssertError(CallToolResult result, string? expectedSubstring = null, string? message = null)
        {
            Assert.IsTrue(result.IsError == true, message ?? "Expected an error result.");
            if (expectedSubstring != null)
            {
                StringAssert.Contains(GetFirstTextContent(result), expectedSubstring);
            }
        }

        /// <summary>
        /// Parses the first text content of a result into a JsonElement root.
        /// </summary>
        protected static JsonElement ParseResultRoot(CallToolResult result)
        {
            string content = GetFirstTextContent(result);
            Assert.IsFalse(string.IsNullOrWhiteSpace(content), "Expected non-empty result content.");
            return JsonDocument.Parse(content).RootElement;
        }

        #endregion
    }
}
