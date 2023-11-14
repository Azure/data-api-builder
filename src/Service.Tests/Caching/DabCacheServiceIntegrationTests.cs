// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services.Cache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Service.Tests.Caching
{
    /// <summary>
    /// Validates direct execution of DabCacheService.GetOrSetAsync(...)
    /// </summary>
    [TestClass]
    public class DabCacheServiceIntegrationTests
    {
        /// <summary>
        /// Validates that the first invocation of the cache service results in a cache miss because
        /// the cache is expected to be empty.
        /// After a cache miss, a call to the the factory method is expected. The factory method
        /// is responsible for calling the database.
        /// </summary>
        [TestMethod]
        public async Task FirstCacheServiceInvocationCallsFactory()
        {
            // Arrange
            MemoryCache memoryCache = new(new MemoryCacheOptions()
            {
                SizeLimit = 1000,
                ExpirationScanFrequency = TimeSpan.FromMilliseconds(100)
            });

            TimeSpan duration = TimeSpan.FromSeconds(1);
            FusionCacheOptions cacheOptions = new()
            {                
                DefaultEntryOptions = new FusionCacheEntryOptions()
                {
                    Duration = duration
                }
            };

            using FusionCache cache = new(cacheOptions, memoryCache);

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select * from MyTable", dataSource: "dataSource1", queryParameters: parameters);
            int cacheEntryTtl = 1;
            using JsonDocument executorJsonResponse = JsonDocument.Parse(@"{""key"": ""value""}");

            Mock<IQueryExecutor> queryExecutor = new();
            List<string>? args = null;
            queryExecutor.Setup(x => x.ExecuteQueryAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, DbConnectionParam>>(),
                It.IsAny<Func<DbDataReader, List<string>?, Task<object>>>(),
                It.IsAny<HttpContext>(),
                args,
                It.IsAny<string>()).Result).Returns(executorJsonResponse);

            //queryExecutor.Setup(x => x.GetJsonResultAsync<JsonDocument>(It.IsAny<DbDataReader>(), null).Result).Returns(executorJsonResponse);
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            DabCacheService dabCache = new(cache: cache, logger: null, httpContextAccessor: httpContextAccessor.Object);

            // Act
            JsonElement? result = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: queryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtl);

            // Assert
            Assert.AreEqual(expected: true, actual: queryExecutor.Invocations.Count is 1);

            IReadOnlyList<object> actualExecuteQueryAsyncArguments = queryExecutor.Invocations[0].Arguments;
            Assert.AreEqual(expected: queryMetadata.QueryText, actual: actualExecuteQueryAsyncArguments[0], message: "QueryText was not passed through to the executor as expected.");
            Assert.AreEqual(expected: queryMetadata.QueryParameters, actual: actualExecuteQueryAsyncArguments[1], message: "Query parameters were not passed through to the executor as expected.");
            Assert.AreEqual(expected: queryMetadata.DataSource, actual: actualExecuteQueryAsyncArguments[5], message: "Data source was not passed through to the executor as expected.");
        }

        // in order to test behavior and not granular literal units of code chunks, think about what it is you are looking for
        // for testing whether the cache entry calculator is working, create the FusionCache instance with options that will illicit failure: cachesize == 1000. and provide cache entry of size 1002.

        // test - second call to getOrSetAsync results in no additional factory calls. -> same data in == same key generated == cache hit
        [TestMethod]
        public async Task SecondCacheServiceInvocation_CacheHit_NoSecondFactoryCall()
        {
            // Arrange
            MemoryCache memoryCache = new(new MemoryCacheOptions()
            {
                SizeLimit = 1000,
                ExpirationScanFrequency = TimeSpan.FromMilliseconds(100)
            });

            TimeSpan duration = TimeSpan.FromSeconds(1);
            FusionCacheOptions cacheOptions = new()
            {
                DefaultEntryOptions = new FusionCacheEntryOptions()
                {
                    Duration = duration
                }
            };

            using FusionCache cache = new(cacheOptions, memoryCache);

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select * from MyTable", dataSource: "dataSource1", queryParameters: parameters);
            int cacheEntryTtl = 1;
            using JsonDocument executorJsonResponse = JsonDocument.Parse(@"{""key"": ""value""}");

            Mock<IQueryExecutor> mockQueryExecutor = new();
            List<string>? args = null;
            HttpContext? httpContext = null;
            mockQueryExecutor.Setup(x => x.ExecuteQueryAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, DbConnectionParam>>(),
                It.IsAny<Func<DbDataReader?, List<string>?, Task<JsonElement?>>>(),
                httpContext,
                args,
                It.IsAny<string>()).Result).Returns(executorJsonResponse.RootElement.Clone());

            Mock<Func<DbDataReader, List<string>?, Task<JsonElement?>>> dataReaderHandler = new();

            Mock<DbDataReader> mockReader = new();
            mockQueryExecutor.Setup(x => x.GetJsonResultAsync<JsonElement?>(mockReader.Object, null)).Returns(dataReaderHandler.Object);

            Mock<IHttpContextAccessor> httpContextAccessor = new();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
            DabCacheService dabCache = new(cache: cache, logger: null, httpContextAccessor: httpContextAccessor.Object);
            // Prime the cache with a single entry
            _ = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtl);

            // Act
            JsonElement? result = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtl);

            // Assert
            Assert.AreEqual(expected: true, actual: mockQueryExecutor.Invocations.Count is 1);
            Assert.AreEqual(expected: executorJsonResponse.RootElement.Clone().ToString(), actual: result.ToString(), message: "Unexpected result returned by cache service.");
        }

        // test - two calls to getorsetasync results in 1 factory call, sleep 2 seconds -> another getorsetasync call results in a factory execution: cacheEntryOptions honored where cache eviction is handled.
        [TestMethod]
        public async Task ThirdCacheServiceInvocation_CacheHit_NoSecondFactoryCall()
        {
            // Arrange
            MemoryCache memoryCache = new(new MemoryCacheOptions()
            {
                SizeLimit = 1000,
                ExpirationScanFrequency = TimeSpan.FromMilliseconds(100)
            });

            TimeSpan duration = TimeSpan.FromSeconds(1);
            FusionCacheOptions cacheOptions = new()
            {
                DefaultEntryOptions = new FusionCacheEntryOptions()
                {
                    Duration = duration
                }
            };

            using FusionCache cache = new(cacheOptions, memoryCache);

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select * from MyTable", dataSource: "dataSource1", queryParameters: parameters);
            int cacheEntryTtl = 1;
            using JsonDocument executorJsonResponse = JsonDocument.Parse(@"{""key"": ""value""}");

            Mock<IQueryExecutor> mockQueryExecutor = new();
            List<string>? args = null;
            HttpContext? httpContext = null;
            mockQueryExecutor.Setup(x => x.ExecuteQueryAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, DbConnectionParam>>(),
                It.IsAny<Func<DbDataReader?, List<string>?, Task<JsonElement?>>>(),
                httpContext,
                args,
                It.IsAny<string>()).Result).Returns(executorJsonResponse.RootElement.Clone());

            Mock<Func<DbDataReader, List<string>?, Task<JsonElement?>>> dataReaderHandler = new();

            Mock<DbDataReader> mockReader = new();
            mockQueryExecutor.Setup(x => x.GetJsonResultAsync<JsonElement?>(mockReader.Object, null)).Returns(dataReaderHandler.Object);

            Mock<IHttpContextAccessor> httpContextAccessor = new();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
            DabCacheService dabCache = new(cache: cache, logger: null, httpContextAccessor: httpContextAccessor.Object);
            // Prime the cache with a single entry
            _ = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtl);
            _ = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtl);
            Thread.Sleep(1000);

            // Act
            JsonElement? result = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtl);

            // Assert
            Assert.AreEqual(expected: true, actual: mockQueryExecutor.Invocations.Count is 2);
            Assert.AreEqual(expected: executorJsonResponse.RootElement.Clone().ToString(), actual: result.ToString(), message: "Unexpected result returned by cache service.");
        }
        // test - size limit 1KB , call getorsetasync where getcacheEntrySize will estimate > 1KB -> failed cache entry either exception or we'll see that second call to getorsetasync will have second factory call not one.
    }
}
