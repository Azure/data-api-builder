// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services.Cache;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class SqlQueryEngineHelperTests
    {
        private const string DATA_SOURCE_NAME = "default";
        private const string ENTITY_NAME = "Book";

        [DataTestMethod]
        [DataRow("{\"value\":1}", true)]
        [DataRow(null, false)]
        public void ParseResultIntoJsonDocument_HandlesValuesAndNull(string? json, bool hasObject)
        {
            JsonElement? element = json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
            MethodInfo method = typeof(SqlQueryEngine).GetMethod(
                "ParseResultIntoJsonDocument",
                BindingFlags.Static | BindingFlags.NonPublic)!;

            using JsonDocument result = (JsonDocument)method.Invoke(null, new object?[] { element })!;

            Assert.AreEqual(hasObject ? JsonValueKind.Object : JsonValueKind.Null, result.RootElement.ValueKind);
        }

        /// <summary>
        /// Verifies stored-procedure execution returns the first result object and maps empty or absent result arrays to null.
        /// </summary>
        [DataTestMethod]
        [DataRow("[{\"id\":1}]", true, DisplayName = "Populated result returns a document")]
        [DataRow("[]", false, DisplayName = "Empty result returns null")]
        [DataRow(null, false, DisplayName = "Absent result returns null")]
        public async Task ExecuteStoredProcedureCore_HandlesResultShapes(string? json, bool expectsDocument)
        {
            JsonArray? resultArray = json is null ? null : JsonNode.Parse(json)!.AsArray();
            (SqlQueryEngine engine, Mock<IQueryExecutor> executor) = CreateEngine();
            executor.Setup(x => x.ExecuteQueryAsync(
                    It.IsAny<string>(),
                    It.IsAny<IDictionary<string, DbConnectionParam>>(),
                    It.IsAny<Func<DbDataReader, List<string>?, Task<JsonArray>>>(),
                    DATA_SOURCE_NAME,
                    It.IsAny<HttpContext?>(),
                    It.IsAny<List<string>?>()))
                .ReturnsAsync(resultArray!);
            SqlExecuteStructure structure = CreateUninitializedStructure<SqlExecuteStructure>();
            MethodInfo method = typeof(SqlQueryEngine).GetMethod(
                "ExecuteAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(SqlExecuteStructure), typeof(string) },
                modifiers: null)!;

            using JsonDocument? result = await (Task<JsonDocument?>)method.Invoke(
                engine,
                new object[] { structure, DATA_SOURCE_NAME })!;

            Assert.AreEqual(expectsDocument, result is not null);
        }

        /// <summary>
        /// Verifies list execution passes through either the executor's document list or its null result unchanged.
        /// </summary>
        [DataTestMethod]
        [DataRow(true, DisplayName = "Executor returns a document list")]
        [DataRow(false, DisplayName = "Executor returns null")]
        public async Task ExecuteListCore_ReturnsExecutorResult(bool returnList)
        {
            (SqlQueryEngine engine, Mock<IQueryExecutor> executor) = CreateEngine();
            List<JsonDocument>? expected = returnList
                ? new List<JsonDocument> { JsonDocument.Parse("{\"id\":1}") }
                : null;
            executor.Setup(x => x.ExecuteQueryAsync(
                    It.IsAny<string>(),
                    It.IsAny<IDictionary<string, DbConnectionParam>>(),
                    It.IsAny<Func<DbDataReader, List<string>?, Task<List<JsonDocument>>>>(),
                    DATA_SOURCE_NAME,
                    It.IsAny<HttpContext?>(),
                    It.IsAny<List<string>?>()))
                .ReturnsAsync(expected!);
            SqlQueryStructure structure = CreateUninitializedStructure<SqlQueryStructure>();
            MethodInfo method = typeof(SqlQueryEngine).GetMethod(
                "ExecuteListAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(SqlQueryStructure), typeof(string) },
                modifiers: null)!;

            List<JsonDocument>? result = await (Task<List<JsonDocument>?>)method.Invoke(
                engine,
                new object[] { structure, DATA_SOURCE_NAME })!;

            Assert.AreSame(expected, result);
            if (expected is not null)
            {
                foreach (JsonDocument document in expected)
                {
                    document.Dispose();
                }
            }
        }

        private static (SqlQueryEngine Engine, Mock<IQueryExecutor> Executor) CreateEngine()
        {
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            runtimeConfig.UpdateDefaultDataSourceName(DATA_SOURCE_NAME);
            Mock<RuntimeConfigLoader> loader = new(null, null);
            Mock<RuntimeConfigProvider> configProviderMock = new(loader.Object);
            configProviderMock.Setup(x => x.GetConfig()).Returns(runtimeConfig);
            RuntimeConfigProvider configProvider = configProviderMock.Object;
            Mock<IQueryBuilder> queryBuilder = new();
            queryBuilder.Setup(x => x.Build(It.IsAny<SqlExecuteStructure>())).Returns("execute");
            queryBuilder.Setup(x => x.Build(It.IsAny<SqlQueryStructure>())).Returns("select");
            Mock<IQueryExecutor> queryExecutor = new();
            Mock<IAbstractQueryManagerFactory> factory = new();
            factory.Setup(x => x.GetQueryBuilder(DatabaseType.MSSQL)).Returns(queryBuilder.Object);
            factory.Setup(x => x.GetQueryExecutor(DatabaseType.MSSQL)).Returns(queryExecutor.Object);
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            Mock<GQLFilterParser> filterParser = new(configProvider, metadataProviderFactory.Object);
            DefaultHttpContext httpContext = new();

            SqlQueryEngine engine = new(
                factory.Object,
                metadataProviderFactory.Object,
                new HttpContextAccessor { HttpContext = httpContext },
                Mock.Of<IAuthorizationResolver>(),
                filterParser.Object,
                NullLogger<IQueryEngine>.Instance,
                configProvider,
                (DabCacheService)RuntimeHelpers.GetUninitializedObject(typeof(DabCacheService)));
            return (engine, queryExecutor);
        }

        private static T CreateUninitializedStructure<T>()
        {
            T structure = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            SetProperty(structure!, "EntityName", ENTITY_NAME);
            SetProperty(structure!, "Parameters", new Dictionary<string, DbConnectionParam>());
            return structure;
        }

        private static void SetProperty(object target, string name, object value)
        {
            Type? type = target.GetType();
            while (type is not null)
            {
                PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property is not null)
                {
                    property.SetValue(target, value);
                    return;
                }

                FieldInfo? field = type.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field is not null)
                {
                    field.SetValue(target, value);
                    return;
                }

                type = type.BaseType;
            }

            Assert.Fail($"Member {name} was not found.");
        }
    }
}
