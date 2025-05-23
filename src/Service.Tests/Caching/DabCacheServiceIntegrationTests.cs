// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.Cache;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Service.Tests.Caching
{
    /// <summary>
    /// Validates direct execution of DabCacheService.GetOrSetAsync(...)
    /// Indirectly validates that the DabCacheService.CreateKey(...) method
    /// creates consistent key values given the same input. The tests would fail
    /// if key creation was invalid.
    /// </summary>
    [TestClass]
    public class DabCacheServiceIntegrationTests
    {
        private const string ERROR_UNEXPECTED_INVOCATIONS = "Unexpected number of queryExecutor invocations.";
        private const string ERROR_UNEXPECTED_RESULT = "Unexpected result returned by cache service.";
        private const string ERROR_FAILED_ARG_PASSTHROUGH = "arg was not passed through to the executor as expected.";
        private enum ExecutorReturnType { Json, Null, Exception, NonNullableJson };

        /// <summary>
        /// Validates that the first invocation of the cache service results in a cache miss because
        /// the cache is expected to be empty.
        /// After a cache miss, a call to the the factory method is expected. The factory method
        /// is responsible for calling the database.
        /// Validates that the cache service passes the expected metadata values through to the query executor
        /// which indicates that the cache service does not modify request metadata.
        /// </summary>
        [TestMethod]
        public async Task FirstCacheServiceInvocationCallsFactory()
        {
            // Arrange
            using FusionCache cache = CreateFusionCache(sizeLimit: 1000, defaultEntryTtlSeconds: 1);
            string expectedDatabaseResponseJson = @"{""key"": ""value""}";
            Mock<IQueryExecutor> mockQueryExecutor = CreateMockQueryExecutor(rawJsonResponse: expectedDatabaseResponseJson, ExecutorReturnType.Json);

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select * from MyTable", dataSource: "dataSource1", queryParameters: parameters);
            DabCacheService dabCache = CreateDabCacheService(cache);

            // Act
            int cacheEntryTtlInSeconds = 1;
            JsonElement? result = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Assert
            Assert.AreEqual(expected: true, actual: mockQueryExecutor.Invocations.Count is 1, message: ERROR_UNEXPECTED_INVOCATIONS);

            // Validates that the expected database response is returned by the cache service.
            Assert.AreEqual(expected: expectedDatabaseResponseJson, actual: result.ToString(), message: ERROR_UNEXPECTED_RESULT);

            // Validates the values of arguments passed to the mock ExecuteQueryAsync method.
            IReadOnlyList<object> actualExecuteQueryAsyncArguments = mockQueryExecutor.Invocations[0].Arguments;
            Assert.AreEqual(expected: queryMetadata.QueryText, actual: actualExecuteQueryAsyncArguments[0], message: "QueryText " + ERROR_FAILED_ARG_PASSTHROUGH);
            Assert.AreEqual(expected: queryMetadata.QueryParameters, actual: actualExecuteQueryAsyncArguments[1], message: "Query parameters " + ERROR_FAILED_ARG_PASSTHROUGH);
            Assert.AreEqual(expected: queryMetadata.DataSource, actual: actualExecuteQueryAsyncArguments[3], message: "Data source " + ERROR_FAILED_ARG_PASSTHROUGH);
        }

        /// <summary>
        /// Validates that a cache hit occurs when the same request is submitted before the cache entry expires.
        /// Validates that DabCacheService.CreateCacheKey(..) outputs the same key given constant input.
        /// </summary>
        [TestMethod]
        public async Task SecondCacheServiceInvocation_CacheHit_NoSecondFactoryCall()
        {
            // Arrange
            using FusionCache cache = CreateFusionCache(sizeLimit: 1000, defaultEntryTtlSeconds: 1);
            string expectedDatabaseResponseJson = @"{""key"": ""value""}";
            Mock<IQueryExecutor> mockQueryExecutor = CreateMockQueryExecutor(rawJsonResponse: expectedDatabaseResponseJson, ExecutorReturnType.Json);

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select * from MyTable", dataSource: "dataSource1", queryParameters: parameters);
            DabCacheService dabCache = CreateDabCacheService(cache);

            // Prime the cache with a single entry
            int cacheEntryTtlInSeconds = 1;
            _ = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Act
            JsonElement? result = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Assert
            Assert.IsFalse(mockQueryExecutor.Invocations.Count is 2, message: "Expected a cache hit, but observed two cache misses.");
            Assert.AreEqual(expected: true, actual: mockQueryExecutor.Invocations.Count is 1, message: ERROR_UNEXPECTED_INVOCATIONS);
            Assert.AreEqual(expected: expectedDatabaseResponseJson, actual: result.ToString(), message: ERROR_UNEXPECTED_RESULT);
        }

        // Validates that the provided cacheEntryOptions are honored by checking the number of factory method invocations within.
        // CacheService.GetOrSetAsync(...)
        // 1st Invocation: Call factory and save result to cache
        // 2nd Invocation: Return result from cache.
        // (1 second pause)
        // 3rd Invocation: Call factory since cache entry evicted.
        [TestMethod]
        public async Task ThirdCacheServiceInvocation_CacheHit_NoSecondFactoryCall()
        {
            // Arrange
            using FusionCache cache = CreateFusionCache(sizeLimit: 1000, defaultEntryTtlSeconds: 1);
            string expectedDatabaseResponseJson = @"{""key"": ""value""}";
            Mock<IQueryExecutor> mockQueryExecutor = CreateMockQueryExecutor(rawJsonResponse: expectedDatabaseResponseJson, ExecutorReturnType.Json);

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select * from MyTable", dataSource: "dataSource1", queryParameters: parameters);
            DabCacheService dabCache = CreateDabCacheService(cache);

            // Prime the cache with a single entry
            int cacheEntryTtlInSeconds = 1;
            _ = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);
            _ = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Sleep for the amount of time the cache entry is valid to trigger eviction.
            Thread.Sleep(millisecondsTimeout: cacheEntryTtlInSeconds * 1000);

            // Act
            JsonElement? result = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Assert
            Assert.IsFalse(mockQueryExecutor.Invocations.Count is 1, message: "QueryExecutor invocation count too low. A cache hit shouldn't have occurred since the entry should have expired.");
            Assert.IsFalse(mockQueryExecutor.Invocations.Count is 3, message: "Unexpected cache misses. The cache entry was never used as the factory method was called on every cache access attempt.");
            Assert.AreEqual(expected: true, actual: mockQueryExecutor.Invocations.Count is 2, message: ERROR_UNEXPECTED_INVOCATIONS);
            Assert.AreEqual(expected: expectedDatabaseResponseJson, actual: result.ToString(), message: ERROR_UNEXPECTED_RESULT);
        }

        /// <summary>
        /// This test validates the behavior of the DabCacheService when a cache entry is larger than the cache capacity.
        /// Since the cache entry's size is larger than the cache size limit (defined when creating Fusion Cache),
        /// the cache entry will not be saved in the cache. Consequently, the database is queried for both requests.
        /// This test asserts the mockQueryExecutor is invoked twice, once for each request.
        /// </summary>
        [TestMethod]
        public async Task LargeCacheKey_BadBehavior()
        {
            // Arrange
            using FusionCache cache = CreateFusionCache(sizeLimit: 2, defaultEntryTtlSeconds: 1);
            string expectedDatabaseResponseJson = @"{""key"": ""value""}";
            Mock<IQueryExecutor> mockQueryExecutor = CreateMockQueryExecutor(rawJsonResponse: expectedDatabaseResponseJson, ExecutorReturnType.Json);

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select * from MyTable", dataSource: "dataSource1", queryParameters: parameters);
            DabCacheService dabCache = CreateDabCacheService(cache);

            // Prime the cache.
            int cacheEntryTtlInSeconds = 1;
            _ = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Act
            JsonElement? result = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Assert
            Assert.IsFalse(mockQueryExecutor.Invocations.Count is 1, message: "Unexpected cache hit when cache entry size exceeded cache capacity.");
            Assert.IsTrue(mockQueryExecutor.Invocations.Count is 2, message: ERROR_UNEXPECTED_INVOCATIONS);
            Assert.AreEqual(expected: expectedDatabaseResponseJson, actual: result.ToString(), message: ERROR_UNEXPECTED_RESULT);
        }

        /// <summary>
        /// Validates that the DabCacheService gracefully reacts to the factory method returning null.
        /// Also ensures the cache service returns that same null value to the caller. 
        /// </summary>
        [TestMethod]
        public async Task CacheServiceFactoryInvocationReturnsNull()
        {
            // Arrange
            using FusionCache cache = CreateFusionCache(sizeLimit: 1000, defaultEntryTtlSeconds: 1);

            string expectedDatabaseResponseJson = @"{""key"": ""value""}";
            Mock<IQueryExecutor> mockQueryExecutor = CreateMockQueryExecutor(rawJsonResponse: expectedDatabaseResponseJson, ExecutorReturnType.Null);

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select * from MyTable", dataSource: "dataSource1", queryParameters: parameters);
            DabCacheService dabCache = CreateDabCacheService(cache);

            // Act
            int cacheEntryTtlInSeconds = 1;
            JsonElement? result = await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Assert
            Assert.AreEqual(expected: true, actual: mockQueryExecutor.Invocations.Count is 1, message: ERROR_UNEXPECTED_INVOCATIONS);

            // Get and validate the arguments passed to the mock ExecuteQueryAsync method.
            IReadOnlyList<object> actualExecuteQueryAsyncArguments = mockQueryExecutor.Invocations[0].Arguments;
            Assert.AreEqual(expected: queryMetadata.QueryText, actual: actualExecuteQueryAsyncArguments[0], message: "QueryText " + ERROR_FAILED_ARG_PASSTHROUGH);
            Assert.AreEqual(expected: queryMetadata.QueryParameters, actual: actualExecuteQueryAsyncArguments[1], message: "Query parameters " + ERROR_FAILED_ARG_PASSTHROUGH);
            Assert.AreEqual(expected: queryMetadata.DataSource, actual: actualExecuteQueryAsyncArguments[3], message: "Data source " + ERROR_FAILED_ARG_PASSTHROUGH);

            // Validate that the null value retrned by the factory method is propogated through to and returned by the cache service.
            Assert.AreEqual(expected: null, actual: result, message: "Expected factory to return a null result.");
        }

        /// <summary>
        /// Validates that the DabCacheService throws an exception when the factory method throws an exception.
        /// In other words, validates that exceptions aren't lost and are propagated to the caller.
        /// </summary>
        [TestMethod]
        public async Task CacheServiceFactoryInvocationThrowsException()
        {
            // Arrange
            using FusionCache cache = CreateFusionCache(sizeLimit: 1000, defaultEntryTtlSeconds: 1);
            string expectedDatabaseResponseJson = @"{""key"": ""value""}";

            // Create mock query executor which raises an exception when called.
            Mock<IQueryExecutor> mockQueryExecutor = CreateMockQueryExecutor(rawJsonResponse: expectedDatabaseResponseJson, ExecutorReturnType.Exception);

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select * from MyTable", dataSource: "dataSource1", queryParameters: parameters);
            DabCacheService dabCache = CreateDabCacheService(cache);

            // Act and Assert
            int cacheEntryTtlInSeconds = 1;
            await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                async () => await dabCache.GetOrSetAsync<JsonElement?>(queryExecutor: mockQueryExecutor.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds),
                message: "Expected an exception to be thrown.");
        }

        /// <summary>
        /// Tests DAB's cache service invocation when the type is JsonArray.
        /// JsonArray aligns with the type used for executing stored procedures against
        /// MSSQL databases.
        /// This test validates that the cache service returns the expected database response
        /// because the cache is empty and the factory method is expected to be called.
        /// </summary>
        [TestMethod]
        public async Task JsonArray_CacheServiceInvocation_CacheEmpty_ReturnsFactoryResult()
        {
            // Arrange
            using FusionCache cache = CreateFusionCache(sizeLimit: 1000, defaultEntryTtlSeconds: 1);
            JsonArray? expectedDatabaseResponse = new()
            {
                JsonNode.Parse(@"{""key"": ""value""}"),
                JsonNode.Parse(@"{""key"": ""value2""}")
            };

            Mock<Func<Task<JsonArray>>> mockExecuteQuery = new();
            mockExecuteQuery.Setup(e => e.Invoke()).Returns(Task.FromResult(expectedDatabaseResponse));

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select c.name from c", dataSource: "dataSource1", queryParameters: parameters);
            DabCacheService dabCache = CreateDabCacheService(cache);

            // Act
            int cacheEntryTtlInSeconds = 1;
            JsonArray? result = await dabCache.GetOrSetAsync<JsonArray>(executeQueryAsync: mockExecuteQuery.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Assert
            Assert.AreEqual(expected: true, actual: mockExecuteQuery.Invocations.Count is 1, message: ERROR_UNEXPECTED_INVOCATIONS);

            // Validates that the expected database response is returned by the cache service.
            Assert.AreEqual(expected: expectedDatabaseResponse, actual: result, message: ERROR_UNEXPECTED_RESULT);
        }

        /// <summary>
        /// Tests DAB's cache service invocation when the type is JsonArray.
        /// JsonArray aligns with the type used for executing stored procedures against
        /// MSSQL databases.
        /// This test validates that a cache hit occurs when the same request
        /// is submitted before the cache entry expires. Validates that
        /// DabCacheService.CreateCacheKey(..) outputs the same key given constant input.
        /// </summary>
        [TestMethod]
        public async Task JsonArray_CacheServiceInvocation_CacheHit_NoFactoryInvocation()
        {
            // Arrange
            using FusionCache cache = CreateFusionCache(sizeLimit: 1000, defaultEntryTtlSeconds: 1);
            JsonArray? expectedDatabaseResponse = new()
            {
                JsonNode.Parse(@"{""key"": ""value""}"),
                JsonNode.Parse(@"{""key"": ""value2""}")
            };

            Mock<Func<Task<JsonArray>>> mockExecuteQuery = new();
            mockExecuteQuery.Setup(e => e.Invoke()).Returns(Task.FromResult(expectedDatabaseResponse));

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select c.name from c", dataSource: "dataSource1", queryParameters: parameters);
            DabCacheService dabCache = CreateDabCacheService(cache);

            int cacheEntryTtlInSeconds = 1;
            // First call. Cache miss
            _ = await dabCache.GetOrSetAsync<JsonArray>(executeQueryAsync: mockExecuteQuery.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Act
            JsonArray? result = await dabCache.GetOrSetAsync<JsonArray>(executeQueryAsync: mockExecuteQuery.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Assert
            Assert.IsFalse(mockExecuteQuery.Invocations.Count > 1, message: "Expected a cache hit, but observed cache misses.");
            Assert.AreEqual(expected: true, actual: mockExecuteQuery.Invocations.Count is 1, message: ERROR_UNEXPECTED_INVOCATIONS);
            Assert.AreEqual(expected: expectedDatabaseResponse, actual: result, message: ERROR_UNEXPECTED_RESULT);
        }

        /// <summary>
        /// Validates that the first invocation of the cache service results in a cache miss because
        /// the cache is expected to be empty.
        /// After a cache miss, Func invocation is expected.
        /// Func is referencing the method which will execute DB query
        /// </summary>
        [TestMethod]
        public async Task FirstCacheServiceInvocationCallsFuncAndReturnResult()
        {
            // Arrange
            using FusionCache cache = CreateFusionCache(sizeLimit: 1000, defaultEntryTtlSeconds: 1);
            JObject expectedDatabaseResponse = JObject.Parse(@"{""key"": ""value""}");

            Mock<Func<Task<JObject>>> mockExecuteQuery = new();
            mockExecuteQuery.Setup(e => e.Invoke()).Returns(Task.FromResult(expectedDatabaseResponse));

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select c.name from c", dataSource: "dataSource1", queryParameters: parameters);
            DabCacheService dabCache = CreateDabCacheService(cache);

            // Act
            int cacheEntryTtlInSeconds = 1;
            JObject? result = await dabCache.GetOrSetAsync<JObject>(executeQueryAsync: mockExecuteQuery.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Assert
            Assert.AreEqual(expected: true, actual: mockExecuteQuery.Invocations.Count is 1, message: ERROR_UNEXPECTED_INVOCATIONS);

            // Validates that the expected database response is returned by the cache service.
            Assert.AreEqual(expected: expectedDatabaseResponse, actual: result, message: ERROR_UNEXPECTED_RESULT);
        }

        /// <summary>
        /// Validates that a cache hit occurs when the same request is submitted before the cache entry expires.
        /// Validates that DabCacheService.CreateCacheKey(..) outputs the same key given constant input.
        /// </summary>
        [TestMethod]
        public async Task SecondCacheServiceInvocation_CacheHit_NoFuncInvocation()
        {
            // Arrange
            using FusionCache cache = CreateFusionCache(sizeLimit: 1000, defaultEntryTtlSeconds: 1);
            JObject expectedDatabaseResponse = JObject.Parse(@"{""key"": ""value""}");

            Mock<Func<Task<JObject>>> mockExecuteQuery = new();
            mockExecuteQuery.Setup(e => e.Invoke()).Returns(Task.FromResult(expectedDatabaseResponse));

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select c.name from c", dataSource: "dataSource1", queryParameters: parameters);
            DabCacheService dabCache = CreateDabCacheService(cache);

            int cacheEntryTtlInSeconds = 1;
            // First call. Cache miss
            _ = await dabCache.GetOrSetAsync<JObject>(executeQueryAsync: mockExecuteQuery.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Act
            JObject? result = await dabCache.GetOrSetAsync<JObject>(executeQueryAsync: mockExecuteQuery.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Assert
            Assert.IsFalse(mockExecuteQuery.Invocations.Count > 1, message: "Expected a cache hit, but observed cache misses.");
            Assert.AreEqual(expected: true, actual: mockExecuteQuery.Invocations.Count is 1, message: ERROR_UNEXPECTED_INVOCATIONS);
            Assert.AreEqual(expected: expectedDatabaseResponse, actual: result, message: ERROR_UNEXPECTED_RESULT);
        }

        // Validates that the provided cacheEntryOptions are honored by checking the number of Func Invocations within.
        // CacheService.GetOrSetAsync(...)
        // 1st Invocation: Invoke func and save result to cache
        // 2nd Invocation: Return result from cache.
        // (1 second pause)
        // 3rd Invocation: Invoke func since cache entry evicted.
        [TestMethod]
        public async Task ThirdCacheServiceInvocation_CacheHit_NoFuncInvocation()
        {
            // Arrange
            using FusionCache cache = CreateFusionCache(sizeLimit: 1000, defaultEntryTtlSeconds: 1);
            JObject expectedDatabaseResponse = JObject.Parse(@"{""key"": ""value""}");

            Mock<Func<Task<JObject>>> mockExecuteQuery = new();
            mockExecuteQuery.Setup(e => e.Invoke()).Returns(Task.FromResult(expectedDatabaseResponse));

            Dictionary<string, DbConnectionParam> parameters = new()
            {
                {"param1", new DbConnectionParam(value: "param1Value") }
            };

            DatabaseQueryMetadata queryMetadata = new(queryText: "select c.name from c", dataSource: "dataSource1", queryParameters: parameters);
            DabCacheService dabCache = CreateDabCacheService(cache);

            int cacheEntryTtlInSeconds = 1;

            // First call. Cache miss
            _ = await dabCache.GetOrSetAsync<JObject>(executeQueryAsync: mockExecuteQuery.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);
            _ = await dabCache.GetOrSetAsync<JObject>(executeQueryAsync: mockExecuteQuery.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Sleep for the amount of time the cache entry is valid to trigger eviction.
            Thread.Sleep(millisecondsTimeout: cacheEntryTtlInSeconds * 1000);

            // Act
            JObject? result = await dabCache.GetOrSetAsync<JObject>(executeQueryAsync: mockExecuteQuery.Object, queryMetadata: queryMetadata, cacheEntryTtl: cacheEntryTtlInSeconds);

            // Assert
            Assert.IsFalse(mockExecuteQuery.Invocations.Count < 2, message: "QueryExecutor invocation count too low. A cache hit shouldn't have occurred since the entry should have expired.");
            Assert.IsFalse(mockExecuteQuery.Invocations.Count > 2, message: "Unexpected cache misses. The cache entry was never used as the factory method was called on every cache access attempt.");
            Assert.AreEqual(expected: expectedDatabaseResponse, actual: result, message: ERROR_UNEXPECTED_RESULT);
        }

        /// <summary>
        /// Validates that the cache works correctly when the request headers include the cache control option, "no-cache."
        /// In this scenario we do not get from the cache, but we still store the retrieved value in the cache, updating
        /// it's value. Therefore, we first set the cache to an empty value with the same cache key as is used in the test,
        /// and we validate that we get our expected value back as a result as well as stored in the cache.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task RequestHeaderContainsCacheControlOptionNoCache()
        {
            // Arrange
            bool isMultipleCreateOperation = false;
            string cacheControlOption = SqlQueryStructure.CACHE_CONTROL_NO_CACHE;
            string entityName = "TestEntity";
            string dataSourceName = "dataSource1";
            string queryText = "select * from MyTable";
            string key = "key";
            string value = "value";
            string expectedDatabaseResponse = $@"{{""{key}"": ""{value}""}}";
            DatabaseQueryMetadata queryMetadata = new(queryText: queryText, dataSource: dataSourceName, queryParameters: new());
            using FusionCache cache = CreateFusionCache(sizeLimit: 1000, defaultEntryTtlSeconds: 60);
            DabCacheService dabCache = CreateDabCacheService(cache);
            dabCache.Set<JsonElement>(queryMetadata, cacheEntryTtl: 60, cacheValue: new JsonElement());
            SqlQueryEngine queryEngine = CreateQueryEngine(dabCache, queryText, expectedDatabaseResponse, entityName);
            Mock<SqlQueryStructure> mockStructure = CreateMockSqlQueryStructure(entityName, dataSourceName, cacheControlOption);

            // Act
            // We are testing a private method in the SqlQuery Engine, therefore use reflection to
            // get the information for the method we will need to invoke for testing.
            MethodInfo? method = typeof(SqlQueryEngine).GetMethod(
                "ExecuteAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(SqlQueryStructure), typeof(string), typeof(bool) },
                modifiers: null
            );

            JsonDocument? result = await (Task<JsonDocument?>)method!.Invoke(
                queryEngine,
                // Elements of this array are the arguments passed to ExecuteAsync.
                new object[]
                {
                    mockStructure.Object,
                    dataSourceName,
                    isMultipleCreateOperation
                }
            )!;

            JsonElement? cachedResult = dabCache.TryGet<JsonElement>(queryMetadata);

            // Assert
            // Validates that the expected database response is returned by the query engine and is correct within the cache service.
            Assert.AreEqual(expected: value, actual: result!.RootElement.GetProperty(key).GetString());
            Assert.AreEqual(expected: expectedDatabaseResponse, actual: cachedResult.ToString());
        }

        /// <summary>
        /// Validates that the cache works correctly when the request headers include the cache control option, "no-store."
        /// In this scenario we do not store the response in the cache. We therefore execute our query and then validate
        /// that the cache remains empty.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task RequestHeaderContainsCacheControlOptionNoStore()
        {
            // Arrange
            bool isMultipleCreateOperation = false;
            string cacheControlOption = SqlQueryStructure.CACHE_CONTROL_NO_STORE;
            string entityName = "TestEntity";
            string dataSourceName = "dataSource1";
            string queryText = "select * from MyTable";
            string key = "key";
            string value = "value";
            string expectedDatabaseResponse = $@"{{""{key}"": ""{value}""}}";
            DatabaseQueryMetadata queryMetadata = new(queryText: queryText, dataSource: dataSourceName, queryParameters: new());
            using FusionCache cache = CreateFusionCache(sizeLimit: 1000, defaultEntryTtlSeconds: 60);
            DabCacheService dabCache = CreateDabCacheService(cache);
            SqlQueryEngine queryEngine = CreateQueryEngine(dabCache, queryText, expectedDatabaseResponse, entityName);
            Mock<SqlQueryStructure> mockStructure = CreateMockSqlQueryStructure(entityName, dataSourceName, cacheControlOption);

            // Act
            // We are testing a private method in the SqlQuery Engine, therefore use reflection to
            // get the information for the method we will need to invoke for testing.
            MethodInfo? method = typeof(SqlQueryEngine).GetMethod(
                "ExecuteAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(SqlQueryStructure), typeof(string), typeof(bool) },
                modifiers: null
            );

            JsonDocument? result = await (Task<JsonDocument?>)method!.Invoke(
                queryEngine,

                // Elements of this array are the arguments passed to ExecuteAsync.
                new object[]
                {
                    mockStructure.Object,
                    dataSourceName,
                    isMultipleCreateOperation
                })!;

            MaybeValue<JsonElement>? cachedResult = dabCache.TryGet<JsonElement>(queryMetadata);

            // Assert
            // Validates that the expected database response is returned by the query engine and that nothing was cached.
            Assert.AreEqual(expected: value, actual: result!.RootElement.GetProperty(key).GetString());
            // Validates outer wrapper was instantiated correctly.
            Assert.AreEqual(expected: true, actual: cachedResult.HasValue);
            // Validates no value returned from cache.
            Assert.AreEqual(expected: false, actual: cachedResult!.Value.HasValue);
        }

        /// <summary>
        /// Validates that the cache works correctly when the request headers include the cache control option, "only-if-cached."
        /// In this scenario we only return a value if it exists in the cache, and in all other cases we throw an exception. Therefore,
        /// we first validate that the correct exception is returned, we then store something in the cache, and finally we validate
        /// that we are able to retrieve that correct value from the cache.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task RequestHeaderContainsCacheControlOptionOnlyIfCached()
        {
            // Arrange
            bool isMultipleCreateOperation = false;
            string cacheControlOption = SqlQueryStructure.CACHE_CONTROL_ONLY_IF_CACHED;
            string entityName = "TestEntity";
            string dataSourceName = "dataSource1";
            string queryText = "select * from MyTable";
            string key = "key";
            string value = "value";
            string expectedDatabaseResponse = $@"{{""{key}"": ""{value}""}}";
            using FusionCache cache = CreateFusionCache(sizeLimit: 1000, defaultEntryTtlSeconds: 60);
            DabCacheService dabCache = CreateDabCacheService(cache);
            SqlQueryEngine queryEngine = CreateQueryEngine(dabCache, queryText, expectedDatabaseResponse, entityName);
            Mock<SqlQueryStructure> mockStructure = CreateMockSqlQueryStructure(entityName, dataSourceName, cacheControlOption);

            // Act
            // We are testing a private method in the SqlQuery Engine, therefore use reflection to
            // get the information for the method we will need to invoke for testing.
            MethodInfo? method = typeof(SqlQueryEngine).GetMethod(
                "ExecuteAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(SqlQueryStructure), typeof(string), typeof(bool) },
                modifiers: null
            );

            JsonDocument? result;
            try
            {
                result = await (Task<JsonDocument?>)method!.Invoke(
                    queryEngine,

                    // Elements of this array are the arguments passed to ExecuteAsync.
                    new object[]
                    {
                        mockStructure.Object,
                        dataSourceName,
                        isMultipleCreateOperation
                    }
                )!;
            }
            catch (DataApiBuilderException dabEx)
            {

                // Assert
                // Validates correct exception is returned for cache miss.
                Assert.AreEqual(expected: HttpStatusCode.GatewayTimeout, actual: dabEx.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ItemNotFound, dabEx.SubStatusCode);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Unexpected exception type thrown: {ex.GetType().Name}. Message: {ex.Message}");
            }

            // Act
            // We now store a value in the cache and validate that we can again call the QueryExecutor with
            // CACHE_CONTROL_ONLY_IF_CACHED but this time retrieve the expected value instead of the previous dab exception. 
            Mock<SqlQueryStructure> mockStructureToSetCache = CreateMockSqlQueryStructure(entityName, dataSourceName, SqlQueryStructure.CACHE_CONTROL_NO_CACHE);
            result = await (Task<JsonDocument?>)method!.Invoke(
                queryEngine,

                // Elements of this array are the arguments passed to ExecuteAsync.
                new object[]
                {
                    mockStructureToSetCache.Object,
                    dataSourceName,
                    isMultipleCreateOperation
                }
            )!;

            result = await (Task<JsonDocument?>)method!.Invoke(
                queryEngine,

                // Elements of this array are the arguments passed to ExecuteAsync.
                new object[]
                {
                    mockStructure.Object,
                    dataSourceName,
                    isMultipleCreateOperation
                }
            )!;

            // Assert
            // Validates that the expected database response is returned by the query engine.
            Assert.AreEqual(expected: value, actual: result!.RootElement.GetProperty(key).GetString());
        }

        private static Mock<SqlQueryStructure> CreateMockSqlQueryStructure(string entityName, string dataSourceName, string? cacheControlOption)
        {
            Dictionary<EntityActionOperation, string?> mockPolicyDict = new()
            {
                { EntityActionOperation.Read, string.Empty }
            };

            SourceDefinition sourceDefinition = new()
            {
                PrimaryKey = new List<string>()
            };

            bool isValidateOnly = false;
            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[SqlQueryStructure.CACHE_CONTROL] = cacheControlOption;
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = string.Empty;
            Dictionary<string, DatabaseObject> entityToDatabaseObject = new();
            entityToDatabaseObject.Add(entityName, new DatabaseTable());

            Mock<RuntimeConfigProvider> mockRuntimeConfigProvider = CreateMockRuntimeConfigProvider(entityName);
            Mock<IAbstractQueryManagerFactory> mockQueryFactory = new();
            Mock<ILogger<ISqlMetadataProvider>> mockLogger = new();
            Mock<MsSqlMetadataProvider> mockSqlMetadataProvider = new(
                mockRuntimeConfigProvider.Object,
                mockQueryFactory.Object,
                mockLogger.Object,
                dataSourceName,
                isValidateOnly);
            mockSqlMetadataProvider
                .Setup(s => s.EntityToDatabaseObject)
                .Returns(entityToDatabaseObject);
            Mock<IMetadataProviderFactory> mockMetadataProviderFactory = new();
            Mock<IAuthorizationResolver> mockAuthorizationResolver = new();
            Mock<RestRequestContext> mockRestRequestContext = new(
                entityName,
                new DatabaseTable());
            mockRestRequestContext
                .Setup(r => r.PrimaryKeyValuePairs)
                .Returns(new Dictionary<string, object>());
            Mock<GQLFilterParser> mockFilterParser = new(
                mockRuntimeConfigProvider.Object,
                mockMetadataProviderFactory.Object);
            Mock<SqlQueryStructure> mockStructure = new(
                mockRestRequestContext.Object,
                mockSqlMetadataProvider.Object,
                mockAuthorizationResolver.Object,
                mockRuntimeConfigProvider.Object,
                mockFilterParser.Object,
                httpContext);
            mockStructure.Setup(s => s.CacheControlOption).Returns(cacheControlOption);
            mockStructure.Setup(s => s.DbPolicyPredicatesForOperations).Returns(mockPolicyDict);
            mockStructure.Setup(s => s.EntityName).Returns(entityName);
            mockStructure.Setup(s => s.GetUnderlyingSourceDefinition()).Returns(sourceDefinition);

            return mockStructure;
        }

        private static Mock<RuntimeConfigProvider> CreateMockRuntimeConfigProvider(string entityName)
        {
            Entity entity = new(
                Source: new EntitySource(string.Empty, null, null, null),
                GraphQL: new EntityGraphQLOptions(string.Empty, string.Empty),
                Rest: new EntityRestOptions(),
                Permissions: Array.Empty<EntityPermission>(),
                Mappings: new Dictionary<string, string>(),
                Relationships: new Dictionary<string, EntityRelationship>(),
                Cache: new EntityCacheOptions { Enabled = true },
                IsLinkingEntity: false,
                Health: null
            );

            DataSource dataSource = new(DatabaseType.MSSQL, string.Empty);

            RuntimeEntities entities = new(
            new Dictionary<string, Entity>
            {
                { entityName, entity }
            });

            Mock<RuntimeConfig> mockRuntimeConfig = new(
                string.Empty,
                dataSource,
                entities,
                null,
                null
            );
            mockRuntimeConfig
                .Setup(c => c.GetDataSourceFromDataSourceName(It.IsAny<string>()))
                .Returns(dataSource);
            mockRuntimeConfig
                .Setup(c => c.Entities)
                .Returns(entities);
            mockRuntimeConfig
                .Setup(c => c.CanUseCache())
                .Returns(true);
            mockRuntimeConfig
                .Setup(c => c.GetEntityCacheEntryTtl(It.IsAny<string>()))
                .Returns(60);
            Mock<RuntimeConfigLoader> mockLoader = new(null, null);
            Mock<RuntimeConfigProvider> mockRuntimeConfigProvider = new(mockLoader.Object);
            mockRuntimeConfigProvider
                .Setup(provider => provider.GetConfig())
                .Returns(mockRuntimeConfig.Object);
            return mockRuntimeConfigProvider;
        }

        private static SqlQueryEngine CreateQueryEngine(DabCacheService cache, string queryText, string expectedDatabaseResponse, string entityName)
        {
            using JsonDocument executorJsonResponse = JsonDocument.Parse(expectedDatabaseResponse);
            Mock<IQueryExecutor> mockQueryExecutor = CreateMockQueryExecutor(expectedDatabaseResponse, ExecutorReturnType.NonNullableJson);
            Mock<IQueryBuilder> mockQueryBuilder = new();
            mockQueryBuilder
                .Setup(builder => builder.Build(It.IsAny<SqlQueryStructure>()))
                .Returns(queryText);
            Mock<IAbstractQueryManagerFactory> mockQueryFactory = new();
            mockQueryFactory
                .Setup(factory => factory.GetQueryExecutor(It.IsAny<DatabaseType>()))
                .Returns(mockQueryExecutor.Object);
            mockQueryFactory
                .Setup(factory => factory.GetQueryBuilder(It.IsAny<DatabaseType>()))
                .Returns(mockQueryBuilder.Object);
            Mock<IMetadataProviderFactory> mockMetadataProviderFactory = new();
            Mock<IHttpContextAccessor> mockHttpContextAccessor = new();
            Mock<IAuthorizationResolver> mockAuthorizationResolver = new();
            Mock<ILogger<IQueryEngine>> mockLogger = new();
            Mock<RuntimeConfigProvider> mockRuntimeConfigProvider = CreateMockRuntimeConfigProvider(entityName);
            Mock<GQLFilterParser> mockFilterParser = new(mockRuntimeConfigProvider.Object, mockMetadataProviderFactory.Object);

            return new(
                mockQueryFactory.Object,
                mockMetadataProviderFactory.Object,
                mockHttpContextAccessor.Object,
                mockAuthorizationResolver.Object,
                mockFilterParser.Object,
                mockLogger.Object,
                mockRuntimeConfigProvider.Object,
                cache);
        }

        /// <summary>
        /// FusionCache instance which caller is responsible for disposing.
        /// Creates a memorycache instance with the desired options for use within FusionCache.
        /// </summary>
        /// <param name="sizeLimit">Size limit of memory cache in bytes.</param>
        /// <param name="defaultEntryTtlSeconds">Default seconds a cache entry is valid before eviction.</param>
        /// <returns>FusionCache instance which caller is responsible for disposing.</returns>
        /// <seealso cref="https://github.com/ZiggyCreatures/FusionCache/issues/179#issuecomment-1768962446"/>
        private static FusionCache CreateFusionCache(long sizeLimit = 1000, int defaultEntryTtlSeconds = 1)
        {
            MemoryCache memoryCache = new(new MemoryCacheOptions()
            {
                SizeLimit = sizeLimit,
                ExpirationScanFrequency = TimeSpan.FromMilliseconds(100)
            });

            TimeSpan duration = TimeSpan.FromSeconds(defaultEntryTtlSeconds);
            FusionCacheOptions cacheOptions = new()
            {
                DefaultEntryOptions = new FusionCacheEntryOptions()
                {
                    Duration = duration
                }
            };

            return new(cacheOptions, memoryCache);
        }

        /// <summary>
        /// Creates a mock QueryExecutor that is called by DAB's SqlQueryEngine.
        /// Results returned by this query executor simulate results returned by the database.
        /// The QueryExecutor resides at the 'system edge' of DAB code and is the last
        /// component in the request chain prior to passing of request metadata to the database.
        /// Mocked interface methods used explicitly in the SqlQueryEngine:
        /// - ExecuteQueryAsync()
        /// - GetJsonResultAsync()
        /// </summary>
        /// <param name="rawJsonResponse">JSON expected to be returned by the database/factory method.</param>
        /// <param name="executorReturnType">Return type of ExecuteQueryAsync mock.</param>
        /// <returns>Mock implementation of IQueryExecutor</returns>
        private static Mock<IQueryExecutor> CreateMockQueryExecutor(string rawJsonResponse, ExecutorReturnType executorReturnType)
        {
            Mock<IQueryExecutor> mockQueryExecutor = new();

            // The following two arguments are created and initialized to null
            // because arguments in a mocked method can't include named parameters.
            List<string>? args = null;
            HttpContext? httpContext = null;
            using JsonDocument executorJsonResponse = JsonDocument.Parse(rawJsonResponse);

            switch (executorReturnType)
            {
                case ExecutorReturnType.Null:
                    mockQueryExecutor.Setup(x => x.ExecuteQueryAsync(
                        It.IsAny<string>(),
                        It.IsAny<IDictionary<string, DbConnectionParam>>(),
                        It.IsAny<Func<DbDataReader?, List<string>?, Task<JsonElement?>>>(),
                        It.IsAny<string>(),
                        httpContext,
                        args).Result)
                        .Returns((JsonElement?)null);
                    break;
                case ExecutorReturnType.Exception:
                    mockQueryExecutor.Setup(x => x.ExecuteQueryAsync(
                        It.IsAny<string>(),
                        It.IsAny<IDictionary<string, DbConnectionParam>>(),
                        It.IsAny<Func<DbDataReader?, List<string>?, Task<JsonElement?>>>(),
                        It.IsAny<string>(),
                        httpContext,
                        args).Result)
                        .Throws(new DataApiBuilderException(
                            message: "DB ERROR",
                            statusCode: HttpStatusCode.InternalServerError,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed));
                    break;
                case ExecutorReturnType.NonNullableJson:
                    mockQueryExecutor.Setup(x => x.ExecuteQueryAsync<JsonElement>(
                        It.IsAny<string>(),
                        It.IsAny<IDictionary<string, DbConnectionParam>>(),
                        It.IsAny<Func<DbDataReader, List<string>?, Task<JsonElement>>>(),
                        It.IsAny<string>(),
                        It.IsAny<HttpContext?>(),
                        It.IsAny<List<string>?>()))
                        .ReturnsAsync(executorJsonResponse.RootElement.Clone());
                    break;
                case ExecutorReturnType.Json:
                default:
                    mockQueryExecutor.Setup(x => x.ExecuteQueryAsync(
                        It.IsAny<string>(),
                        It.IsAny<IDictionary<string, DbConnectionParam>>(),
                        It.IsAny<Func<DbDataReader?, List<string>?, Task<JsonElement?>>>(),
                        It.IsAny<string>(),
                        httpContext,
                        args).Result)
                        .Returns(executorJsonResponse.RootElement.Clone());
                    break;

            }

            // Create a Mock Func<arg1, arg2, arg3> so when the mock ExecuteQueryAsync method
            // can internally call the dataReaderHandler with the expected arguments.
            // Required to mock the result returned from a factory method. A factory method
            // is executed after a cache miss and makes a direct call to the database.
            Mock<Func<DbDataReader, List<string>?, Task<JsonElement?>>> dataReaderHandler = new();
            Mock<DbDataReader> mockReader = new();
            mockQueryExecutor.Setup(x => x.GetJsonResultAsync<JsonElement?>(mockReader.Object, null)).Returns(dataReaderHandler.Object);

            return mockQueryExecutor;
        }

        /// <summary>
        /// Creates an instance of the DabCacheService.
        /// </summary>
        /// <param name="cache">FusionCache instance</param>
        /// <returns>DabCacheService</returns>
        private static DabCacheService CreateDabCacheService(FusionCache cache)
        {
            HttpContext? httpContext = null;
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

            return new(cache: cache, logger: null, httpContextAccessor: httpContextAccessor.Object);
        }
    }
}
