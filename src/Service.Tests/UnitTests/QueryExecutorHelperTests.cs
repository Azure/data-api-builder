// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class QueryExecutorHelperTests
    {
        [DataTestMethod]
        [DataRow("[1,2]", typeof(JsonArray))]
        [DataRow("{\"value\":1}", typeof(JsonObject))]
        [DataRow("\"value\"", typeof(JsonValue))]
        [DataRow("42", typeof(JsonValue))]
        [DataRow("null", null)]
        public void FromJsonElement_CreatesExpectedNodeType(string json, Type? expectedType)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            MethodInfo method = typeof(QueryExecutor<SqlConnection>).GetMethod(
                "FromJsonElement", BindingFlags.Static | BindingFlags.NonPublic)!;

            JsonNode? result = (JsonNode?)method.Invoke(null, new object[] { document.RootElement });

            if (expectedType is null)
            {
                Assert.IsNull(result);
            }
            else
            {
                Assert.IsInstanceOfType(result, expectedType);
            }
        }

        [DataTestMethod]
        [DataRow(100L, 101L, true)]
        [DataRow(100L, 100L, false)]
        [DataRow(100L, 0L, false)]
        public void ValidateSize_EnforcesOnlyValuesOverLimit(long available, long requested, bool throws)
        {
            MsSqlQueryExecutor executor = (MsSqlQueryExecutor)RuntimeHelpers.GetUninitializedObject(typeof(MsSqlQueryExecutor));
            Type baseType = typeof(MsSqlQueryExecutor).BaseType!;
            baseType.GetField("_maxResponseSizeMB", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(executor, 1);
            MethodInfo method = baseType.GetMethod("ValidateSize", BindingFlags.Instance | BindingFlags.NonPublic)!;

            if (throws)
            {
                TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(
                    () => method.Invoke(executor, new object[] { available, requested }));
                Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
            }
            else
            {
                method.Invoke(executor, new object[] { available, requested });
            }
        }

        [TestMethod]
        public void ResultPropertyHandlers_ReturnReaderState()
        {
            MsSqlQueryExecutor executor = CreateExecutor();
            Mock<DbDataReader> reader = new();
            reader.SetupGet(x => x.RecordsAffected).Returns(3);
            reader.SetupGet(x => x.HasRows).Returns(true);

            Dictionary<string, object> sync = executor.GetResultProperties(reader.Object);
            Dictionary<string, object> asyncResult = executor.GetResultPropertiesAsync(reader.Object).Result;

            Assert.AreEqual(3, sync[nameof(DbDataReader.RecordsAffected)]);
            Assert.AreEqual(true, sync[nameof(DbDataReader.HasRows)]);
            CollectionAssert.AreEquivalent(sync, asyncResult);
        }

        [TestMethod]
        public void StreamCharData_ReadsContentAndHandlesEmptyCells()
        {
            MsSqlQueryExecutor executor = CreateExecutor();
            Mock<DbDataReader> reader = new();
            reader.Setup(x => x.GetChars(0, 0, null, 0, 0)).Returns(3);
            reader.Setup(x => x.GetChars(0, 0, It.IsAny<char[]>(), 0, 3))
                .Callback((int _, long _, char[]? buffer, int _, int _) => "DAB".CopyTo(0, buffer!, 0, 3))
                .Returns(3);
            StringBuilder result = new();

            Assert.AreEqual(3, executor.StreamCharData(reader.Object, 3, result, 0));
            Assert.AreEqual("DAB", result.ToString());

            reader.Setup(x => x.GetChars(0, 0, null, 0, 0)).Returns(0);
            Assert.AreEqual(0, executor.StreamCharData(reader.Object, 0, result, 0));
        }

        [TestMethod]
        public void StreamByteData_ReadsContentAndHandlesEmptyCells()
        {
            MsSqlQueryExecutor executor = CreateExecutor();
            Mock<DbDataReader> reader = new();
            reader.Setup(x => x.GetBytes(0, 0, null, 0, 0)).Returns(2);
            reader.Setup(x => x.GetBytes(0, 0, It.IsAny<byte[]>(), 0, 2))
                .Callback((int _, long _, byte[]? buffer, int _, int _) =>
                {
                    buffer![0] = 1;
                    buffer[1] = 2;
                })
                .Returns(2);

            Assert.AreEqual(2, executor.StreamByteData(reader.Object, 2, 0, out byte[]? bytes));
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, bytes);

            reader.Setup(x => x.GetBytes(0, 0, null, 0, 0)).Returns(0);
            Assert.AreEqual(0, executor.StreamByteData(reader.Object, 0, 0, out bytes));
            Assert.AreEqual(0, bytes!.Length);
        }

        [DataTestMethod]
        [DataRow(typeof(string), 3)]
        [DataRow(typeof(byte[]), 2)]
        [DataRow(typeof(int), 4)]
        public void StreamDataIntoResultSetRow_HandlesSupportedColumnKinds(Type fieldType, int expectedSize)
        {
            MsSqlQueryExecutor executor = CreateExecutor();
            Mock<DbDataReader> reader = new();
            reader.Setup(x => x.GetFieldType(0)).Returns(fieldType);
            reader.Setup(x => x.GetChars(0, 0, null, 0, 0)).Returns(3);
            reader.Setup(x => x.GetChars(0, 0, It.IsAny<char[]>(), 0, 3)).Returns(3);
            reader.Setup(x => x.GetBytes(0, 0, null, 0, 0)).Returns(2);
            reader.Setup(x => x.GetBytes(0, 0, It.IsAny<byte[]>(), 0, 2)).Returns(2);
            reader.Setup(x => x["value"]).Returns(42);
            DbResultSetRow row = new();

            int size = executor.StreamDataIntoDbResultSetRow(reader.Object, row, "value", 4, 0, 10);

            Assert.AreEqual(expectedSize, size);
            Assert.IsTrue(row.Columns.ContainsKey("value"));
        }

        [TestMethod]
        public void AddDbExecutionTime_AccumulatesAndIgnoresMissingContext()
        {
            MsSqlQueryExecutor executor = CreateExecutor();
            DefaultHttpContext context = new();
            typeof(QueryExecutor<SqlConnection>).GetField("<HttpContextAccessor>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(executor, new HttpContextAccessor { HttpContext = context });

            executor.AddDbExecutionTimeToMiddlewareContext(2);
            executor.AddDbExecutionTimeToMiddlewareContext(3);

            Assert.AreEqual(5L, context.Items["TotalDbExecutionTime"]);
            typeof(QueryExecutor<SqlConnection>).GetField("<HttpContextAccessor>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(executor, new HttpContextAccessor());
            executor.AddDbExecutionTimeToMiddlewareContext(1);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task GetJsonResultAsync_HandlesRowsAndNoRows(bool hasRows)
        {
            MsSqlQueryExecutor executor = CreateExecutor();
            Mock<DbDataReader> reader = new();
            reader.SetupGet(x => x.HasRows).Returns(hasRows);
            reader.SetupSequence(x => x.ReadAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(hasRows)
                .ReturnsAsync(false);
            reader.Setup(x => x.GetString(0)).Returns("{\"value\":7}");

            JsonDocument? result = await executor.GetJsonResultAsync<JsonDocument>(reader.Object);

            if (hasRows)
            {
                Assert.AreEqual(7, result!.RootElement.GetProperty("value").GetInt32());
                result.Dispose();
            }
            else
            {
                Assert.IsNull(result);
            }
        }

        [TestMethod]
        public async Task ReadHelpers_TranslateDatabaseExceptions()
        {
            MsSqlQueryExecutor executor = CreateExecutor();
            Mock<DbDataReader> reader = new();
            reader.Setup(x => x.Read()).Throws(new TestDbException());
            reader.Setup(x => x.ReadAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ThrowsAsync(new TestDbException());

            Assert.ThrowsException<DataApiBuilderException>(() => executor.Read(reader.Object));
            await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() => executor.ReadAsync(reader.Object));
        }

        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        public async Task ExtractResultSet_HandlesColumnsNullsAndFiltering(bool useAsync, bool filterOutColumn)
        {
            MsSqlQueryExecutor executor = CreateExecutor(maxResponseSizeEnabled: false);
            Mock<DbDataReader> reader = CreateSingleRowReader(valueIsNull: false);
            List<string>? columns = filterOutColumn ? new List<string> { "other" } : null;

            DbResultSet result = useAsync
                ? await executor.ExtractResultSetFromDbDataReaderAsync(reader.Object, columns)
                : executor.ExtractResultSetFromDbDataReader(reader.Object, columns);

            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual(filterOutColumn ? 0 : 1, result.Rows[0].Columns.Count);
        }

        [TestMethod]
        public async Task ExtractResultSet_HandlesNullAndStreamedValues()
        {
            MsSqlQueryExecutor executor = CreateExecutor(maxResponseSizeEnabled: false);
            Mock<DbDataReader> nullReader = CreateSingleRowReader(valueIsNull: true);

            DbResultSet nullResult = await executor.ExtractResultSetFromDbDataReaderAsync(nullReader.Object);

            Assert.IsNull(nullResult.Rows.Single().Columns["value"]);

            executor = CreateExecutor(maxResponseSizeEnabled: true);
            Mock<DbDataReader> streamedReader = CreateSingleRowReader(valueIsNull: false);
            streamedReader.Setup(x => x.GetFieldType(0)).Returns(typeof(string));
            streamedReader.Setup(x => x.GetChars(0, 0, null, 0, 0)).Returns(3);
            streamedReader.Setup(x => x.GetChars(0, 0, It.IsAny<char[]>(), 0, 3))
                .Callback((int _, long _, char[]? buffer, int _, int _) => "DAB".CopyTo(0, buffer!, 0, 3))
                .Returns(3);

            DbResultSet streamedResult = executor.ExtractResultSetFromDbDataReader(streamedReader.Object);

            Assert.AreEqual("DAB", streamedResult.Rows.Single().Columns["value"]);
        }

        [TestMethod]
        public async Task GetMultipleResultSets_ReturnsFirstPopulatedResultAsUpdate()
        {
            QueryExecutor<SqlConnection> executor = CreateBaseExecutor();
            Mock<DbDataReader> reader = CreateSingleRowReader(valueIsNull: false);

            DbResultSet result = await executor.GetMultipleResultSetsIfAnyAsync(reader.Object);

            Assert.AreEqual(true, result.ResultProperties[SqlMutationEngine.IS_UPDATE_RESULT_SET]);
            reader.Verify(x => x.NextResultAsync(It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [TestMethod]
        public async Task GetMultipleResultSets_ThrowsWhenNeitherMutationProducesRows()
        {
            QueryExecutor<SqlConnection> executor = CreateBaseExecutor();
            Mock<DbDataReader> reader = new();
            reader.SetupGet(x => x.RecordsAffected).Returns(0);
            reader.SetupGet(x => x.HasRows).Returns(false);
            reader.Setup(x => x.ReadAsync(It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(false);
            reader.Setup(x => x.NextResultAsync(It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(false);

            await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                () => executor.GetMultipleResultSetsIfAnyAsync(reader.Object, new List<string> { "id=1", "Book" }));
        }

        [TestMethod]
        public async Task MsSqlGetMultipleResultSets_MissingCountResultThrows()
        {
            MsSqlQueryExecutor executor = CreateExecutor(maxResponseSizeEnabled: false);
            Mock<DbDataReader> reader = new();
            reader.SetupGet(x => x.RecordsAffected).Returns(0);
            reader.SetupGet(x => x.HasRows).Returns(false);
            reader.Setup(x => x.ReadAsync(It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(false);

            await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() =>
                executor.GetMultipleResultSetsIfAnyAsync(reader.Object));
        }

        [TestMethod]
        public async Task MsSqlGetMultipleResultSets_NoMutationResultWithArgumentsThrowsNotFound()
        {
            MsSqlQueryExecutor executor = CreateExecutor(maxResponseSizeEnabled: false);
            DataTable schema = new();
            schema.Columns.Add("ColumnName", typeof(string));
            schema.Columns.Add("ColumnSize", typeof(int));
            schema.Rows.Add(MsSqlQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK, 4);
            Mock<DbDataReader> reader = new();
            reader.SetupGet(x => x.RecordsAffected).Returns(1);
            reader.SetupGet(x => x.HasRows).Returns(true);
            reader.SetupSequence(x => x.ReadAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);
            reader.Setup(x => x.GetSchemaTable()).Returns(schema);
            reader.Setup(x => x.GetOrdinal(MsSqlQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK)).Returns(0);
            reader.Setup(x => x.IsDBNull(0)).Returns(false);
            reader.Setup(x => x[MsSqlQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK]).Returns(0);
            reader.Setup(x => x.GetFieldType(0)).Returns(typeof(int));
            reader.Setup(x => x.NextResultAsync(It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(false);

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() =>
                executor.GetMultipleResultSetsIfAnyAsync(reader.Object, new List<string> { "<id: 1>", "Book" }));

            Assert.AreEqual(System.Net.HttpStatusCode.NotFound, exception.StatusCode);
        }

        [TestMethod]
        public async Task MsSqlGetMultipleResultSets_NoMutationResultWithoutArgumentsThrowsInternalServerError()
        {
            MsSqlQueryExecutor executor = CreateExecutor(maxResponseSizeEnabled: false);
            DataTable schema = new();
            schema.Columns.Add("ColumnName", typeof(string));
            schema.Columns.Add("ColumnSize", typeof(int));
            schema.Rows.Add(MsSqlQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK, 4);
            Mock<DbDataReader> reader = new();
            reader.SetupGet(x => x.RecordsAffected).Returns(1);
            reader.SetupGet(x => x.HasRows).Returns(true);
            reader.SetupSequence(x => x.ReadAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);
            reader.Setup(x => x.GetSchemaTable()).Returns(schema);
            reader.Setup(x => x.GetOrdinal(MsSqlQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK)).Returns(0);
            reader.Setup(x => x.IsDBNull(0)).Returns(false);
            reader.Setup(x => x[MsSqlQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK]).Returns(0);
            reader.Setup(x => x.GetFieldType(0)).Returns(typeof(int));
            reader.Setup(x => x.NextResultAsync(It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(false);

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() =>
                executor.GetMultipleResultSetsIfAnyAsync(reader.Object));

            Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
        }

        [TestMethod]
        public async Task GetJsonResultAsync_StreamsWhenResponseLimitIsEnabled()
        {
            MsSqlQueryExecutor executor = CreateExecutor(maxResponseSizeEnabled: true);
            Mock<DbDataReader> reader = new();
            reader.SetupGet(x => x.HasRows).Returns(true);
            reader.SetupSequence(x => x.ReadAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);
            reader.Setup(x => x.GetChars(0, 0, null, 0, 0)).Returns(11);
            reader.Setup(x => x.GetChars(0, 0, It.IsAny<char[]>(), 0, 11))
                .Callback((int _, long _, char[]? buffer, int _, int _) => "{\"value\":7}".CopyTo(0, buffer!, 0, 11))
                .Returns(11);

            using JsonDocument? result = await executor.GetJsonResultAsync<JsonDocument>(reader.Object);

            Assert.AreEqual(7, result!.RootElement.GetProperty("value").GetInt32());
        }

        private static Mock<DbDataReader> CreateSingleRowReader(bool valueIsNull)
        {
            DataTable schema = new();
            schema.Columns.Add("ColumnName", typeof(string));
            schema.Columns.Add("ColumnSize", typeof(int));
            schema.Rows.Add("value", 10);

            Mock<DbDataReader> reader = new();
            reader.SetupGet(x => x.RecordsAffected).Returns(1);
            reader.SetupGet(x => x.HasRows).Returns(true);
            reader.SetupSequence(x => x.Read()).Returns(true).Returns(false);
            reader.SetupSequence(x => x.ReadAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);
            reader.Setup(x => x.GetSchemaTable()).Returns(schema);
            reader.Setup(x => x.GetOrdinal("value")).Returns(0);
            reader.Setup(x => x.IsDBNull(0)).Returns(valueIsNull);
            reader.Setup(x => x["value"]).Returns(42);
            reader.Setup(x => x.GetFieldType(0)).Returns(typeof(int));
            return reader;
        }

        private static MsSqlQueryExecutor CreateExecutor(bool maxResponseSizeEnabled = false)
        {
            MsSqlQueryExecutor executor = (MsSqlQueryExecutor)RuntimeHelpers.GetUninitializedObject(typeof(MsSqlQueryExecutor));
            Type baseType = typeof(MsSqlQueryExecutor).BaseType!;
            baseType.GetField("_maxResponseSizeMB", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(executor, 1);
            baseType.GetField("_maxResponseSizeBytes", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(executor, 1024L);
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
                Runtime: new RuntimeOptions(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: null,
                    Host: new(Cors: null, Authentication: null, MaxResponseSizeMB: maxResponseSizeEnabled ? 1 : null)));
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            baseType.GetField("<ConfigProvider>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(executor, configProvider);
            baseType.GetField("<QueryExecutorLogger>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(executor, NullLogger<IQueryExecutor>.Instance);
            baseType.GetField("<DbExceptionParser>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(executor, new TestDbExceptionParser(configProvider));
            return executor;
        }

        private static QueryExecutor<SqlConnection> CreateBaseExecutor()
        {
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            return new QueryExecutor<SqlConnection>(
                new TestDbExceptionParser(configProvider),
                NullLogger<IQueryExecutor>.Instance,
                configProvider,
                new HttpContextAccessor(),
                handler: null);
        }

        private sealed class TestDbException : DbException
        {
            public TestDbException()
            {
            }

            public TestDbException(string message)
                : base(message)
            {
            }

            public TestDbException(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }

        private sealed class TestDbExceptionParser : DbExceptionParser
        {
            public TestDbExceptionParser(RuntimeConfigProvider configProvider)
                : base(configProvider)
            {
            }

            public override bool IsTransientException(DbException e) => false;

            public override HttpStatusCode GetHttpStatusCodeForException(DbException e) => HttpStatusCode.InternalServerError;
        }
    }
}
