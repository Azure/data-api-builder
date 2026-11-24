// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Service.Tests.Caching
{
    [TestClass]
    [TestCategory("Unit")]
    public class SourceAwareCacheTests
    {
        private const string ENTITY_NAME = "Notes";
        private const string CHILD_SOURCE = "child";
        private const string QUERY_TEXT = "synthetic query";

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL, false, null)]
        [DataRow(DatabaseType.MSSQL, false, SqlQueryStructure.CACHE_CONTROL_NO_STORE)]
        [DataRow(DatabaseType.MSSQL, false, SqlQueryStructure.CACHE_CONTROL_NO_CACHE)]
        [DataRow(DatabaseType.MSSQL, false, SqlQueryStructure.CACHE_CONTROL_ONLY_IF_CACHED)]
        [DataRow(DatabaseType.DWSQL, false, null)]
        [DataRow(DatabaseType.DWSQL, false, SqlQueryStructure.CACHE_CONTROL_NO_STORE)]
        [DataRow(DatabaseType.DWSQL, false, SqlQueryStructure.CACHE_CONTROL_NO_CACHE)]
        [DataRow(DatabaseType.DWSQL, false, SqlQueryStructure.CACHE_CONTROL_ONLY_IF_CACHED)]
        [DataRow(DatabaseType.MSSQL, true, null)]
        [DataRow(DatabaseType.DWSQL, true, null)]
        public async Task ExecuteAsync_SessionContextEnabledOnChild_BypassesExistingCache(
            DatabaseType type, bool storedProcedure, string? cacheControl)
        {
            RuntimeConfig config = CreateConfig(type, defaultSessionContext: false, storedProcedure);
            using FusionCache fusionCache = new(new FusionCacheOptions());
            HttpContextAccessor accessor = new() { HttpContext = CreateCaller("alice") };
            DabCacheService cache = new(fusionCache, null, accessor);
            Mock<IQueryExecutor> executor = CreateExecutor(accessor);
            SqlQueryEngine engine = CreateEngine(config, cache, accessor, executor.Object, type, storedProcedure);

            AssertRows(Payload(accessor), await ReadAsync(engine, storedProcedure));
            DatabaseQueryMetadata metadata = new(QUERY_TEXT, CHILD_SOURCE, new());

            config.UpdateDataSourceNameToDataSource(CHILD_SOURCE,
                new DataSource(type, string.Empty, new() { ["set-session-context"] = true }));

            foreach (string? caller in new[] { "bob", null })
            {
                accessor.HttpContext = CreateCaller(caller);
                if (cacheControl is not null)
                {
                    accessor.HttpContext.Request.Headers[SqlQueryStructure.CACHE_CONTROL] = cacheControl;
                }

                AssertRows(Payload(accessor), await ReadAsync(engine, storedProcedure));
            }

            Assert.AreEqual(3, executor.Invocations.Count(call => call.Method.Name == nameof(IQueryExecutor.ExecuteQueryAsync)),
                "Each caller must reach the executor after session context is enabled.");
            if (storedProcedure)
            {
                JsonArray? cached = cache.TryGet<JsonArray?>(metadata, EntityCacheLevel.L1)!.Value.Value;
                AssertRows("[{\"owner\":\"alice\"}]", cached!.ToJsonString());
            }
            else
            {
                JsonElement? cached = cache.TryGet<JsonElement?>(metadata, EntityCacheLevel.L1)!.Value.Value;
                AssertRows("[{\"owner\":\"alice\"}]", cached!.Value.GetRawText());
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ExecuteAsync_OnlyDefaultSourceUsesSessionContext_ChildStillCaches(bool storedProcedure)
        {
            RuntimeConfig config = CreateConfig(DatabaseType.MSSQL, defaultSessionContext: true, storedProcedure);
            using FusionCache fusionCache = new(new FusionCacheOptions());
            HttpContextAccessor accessor = new() { HttpContext = CreateCaller("alice") };
            DabCacheService cache = new(fusionCache, null, accessor);
            Mock<IQueryExecutor> executor = CreateExecutor(accessor);
            SqlQueryEngine engine = CreateEngine(config, cache, accessor, executor.Object, DatabaseType.MSSQL, storedProcedure);

            string first = await ReadAsync(engine, storedProcedure);
            string second = await ReadAsync(engine, storedProcedure);

            AssertRows(first, second);
            Assert.AreEqual(1, executor.Invocations.Count(call => call.Method.Name == nameof(IQueryExecutor.ExecuteQueryAsync)),
                "The default source must not disable caching for an eligible child source.");
        }

        private static DefaultHttpContext CreateCaller(string? caller)
        {
            DefaultHttpContext context = new();
            if (caller is not null)
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", caller) }, "synthetic"));
            }

            context.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = caller is null ? "anonymous" : "authenticated";
            return context;
        }

        private static string Payload(IHttpContextAccessor accessor)
        {
            string? caller = accessor.HttpContext!.User.FindFirst("oid")?.Value;
            return caller is null ? "[]" : JsonSerializer.Serialize(new[] { new { owner = caller } });
        }

        private static void AssertRows(string expected, string actual)
        {
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)),
                $"Expected caller-scoped rows {expected}; received {actual}.");
        }

        private static async Task<string> ReadAsync(SqlQueryEngine engine, bool storedProcedure)
        {
            if (storedProcedure)
            {
                StoredProcedureRequestContext request = new(ENTITY_NAME, new DatabaseStoredProcedure(), null, EntityActionOperation.Read);
                IActionResult response = await engine.ExecuteAsync(request, CHILD_SOURCE);
                Assert.IsInstanceOfType<OkObjectResult>(response);
                return JsonSerializer.SerializeToElement(((OkObjectResult)response).Value).GetProperty("value").GetRawText();
            }

            using JsonDocument? result = await engine.ExecuteAsync(new FindRequestContext(ENTITY_NAME, new DatabaseTable(), isList: true));
            Assert.IsNotNull(result, "The synthetic executor must return a JSON document.");
            return result.RootElement.GetRawText();
        }

        private static RuntimeConfig CreateConfig(DatabaseType childType, bool defaultSessionContext, bool storedProcedure)
        {
            DataSource root = new(DatabaseType.MSSQL, string.Empty, new() { ["set-session-context"] = defaultSessionContext });
            DataSource child = new(childType, string.Empty, new() { ["set-session-context"] = false });
            Entity entity = new(
                new EntitySource("notes", storedProcedure ? EntitySourceType.StoredProcedure : EntitySourceType.Table, null, null),
                GraphQL: null!, Fields: null, Rest: null!, Permissions: Array.Empty<EntityPermission>(),
                Mappings: null, Relationships: null, Cache: new EntityCacheOptions(true));
            return new RuntimeConfig(
                string.Empty, root,
                new RuntimeOptions(null, null, null, null, Cache: new RuntimeCacheOptions(true, 300)),
                new RuntimeEntities(new Dictionary<string, Entity> { [ENTITY_NAME] = entity }),
                "root", new() { ["root"] = root, [CHILD_SOURCE] = child }, new() { [ENTITY_NAME] = CHILD_SOURCE });
        }

        private static Mock<IQueryExecutor> CreateExecutor(IHttpContextAccessor accessor)
        {
            Mock<IQueryExecutor> executor = new();
            executor.Setup(x => x.ExecuteQueryAsync(
                It.IsAny<string>(), It.IsAny<IDictionary<string, DbConnectionParam>>(),
                It.IsAny<Func<DbDataReader, List<string>?, Task<JsonElement>>>(),
                CHILD_SOURCE, It.IsAny<HttpContext?>(), It.IsAny<List<string>?>()))
                .ReturnsAsync(() => JsonSerializer.Deserialize<JsonElement>(Payload(accessor)));
            executor.Setup(x => x.ExecuteQueryAsync(
                It.IsAny<string>(), It.IsAny<IDictionary<string, DbConnectionParam>>(),
                It.IsAny<Func<DbDataReader, List<string>?, Task<JsonDocument>>>(),
                CHILD_SOURCE, It.IsAny<HttpContext?>(), It.IsAny<List<string>?>()))
                .ReturnsAsync(() => JsonDocument.Parse(Payload(accessor)));
            executor.Setup(x => x.ExecuteQueryAsync(
                It.IsAny<string>(), It.IsAny<IDictionary<string, DbConnectionParam>>(),
                It.IsAny<Func<DbDataReader, List<string>?, Task<JsonArray>>>(),
                CHILD_SOURCE, It.IsAny<HttpContext?>(), It.IsAny<List<string>?>()))
                .ReturnsAsync(() => JsonNode.Parse(Payload(accessor))!.AsArray());
            return executor;
        }

        private static SqlQueryEngine CreateEngine(
            RuntimeConfig config, DabCacheService cache, IHttpContextAccessor accessor, IQueryExecutor executor,
            DatabaseType type, bool storedProcedure)
        {
            Mock<RuntimeConfigLoader> loader = new(null, null);
            loader.Object.RuntimeConfig = config;
            RuntimeConfigProvider provider = new(loader.Object);
            DatabaseObject databaseObject = storedProcedure
                ? new DatabaseStoredProcedure("dbo", "notes") { StoredProcedureDefinition = new(), SourceType = EntitySourceType.StoredProcedure }
                : new DatabaseTable("dbo", "notes") { TableDefinition = new() };
            Mock<ISqlMetadataProvider> metadata = new();
            metadata.Setup(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject> { [ENTITY_NAME] = databaseObject });
            metadata.Setup(x => x.GetSourceDefinition(ENTITY_NAME)).Returns(databaseObject.SourceDefinition);
            metadata.Setup(x => x.GetStoredProcedureDefinition(ENTITY_NAME)).Returns(new StoredProcedureDefinition());
            metadata.Setup(x => x.GetDatabaseType()).Returns(type);
            Mock<IMetadataProviderFactory> metadataFactory = new();
            metadataFactory.Setup(x => x.GetMetadataProvider(CHILD_SOURCE)).Returns(metadata.Object);
            Mock<IAuthorizationResolver> authorization = new();
            authorization.Setup(x => x.ResolveDBPolicy(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>(), It.IsAny<HttpContext>()))
                .Returns(ResolvedDatabasePolicy.Empty);
            GQLFilterParser parser = new(provider, metadataFactory.Object);
            Mock<IQueryBuilder> builder = new();
            builder.Setup(x => x.Build(It.IsAny<SqlQueryStructure>())).Returns(QUERY_TEXT);
            builder.Setup(x => x.Build(It.IsAny<SqlExecuteStructure>())).Returns(QUERY_TEXT);
            Mock<IAbstractQueryManagerFactory> factory = new();
            factory.Setup(x => x.GetQueryExecutor(type)).Returns(executor);
            factory.Setup(x => x.GetQueryBuilder(type)).Returns(builder.Object);
            return new SqlQueryEngine(factory.Object, metadataFactory.Object, accessor, authorization.Object,
                parser, NullLogger<IQueryEngine>.Instance, provider, cache);
        }
    }
}
