// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlQueryExecutorHelperTests
    {
        [TestMethod]
        public async Task GetMultipleResultSets_MissingCountMetadataThrowsInternalServerError()
        {
            PostgreSqlQueryExecutor executor = CreateExecutor();
            Mock<DbDataReader> reader = new();
            reader.Setup(x => x.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() =>
                executor.GetMultipleResultSetsIfAnyAsync(reader.Object));

            Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
        }

        [TestMethod]
        public async Task GetMultipleResultSets_FallbackUpdateWithoutArgumentsThrowsInternalServerError()
        {
            PostgreSqlQueryExecutor executor = CreateExecutor();
            DataTable schema = new();
            schema.Columns.Add("ColumnName", typeof(string));
            schema.Columns.Add("ColumnSize", typeof(int));
            schema.Rows.Add(PostgresQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK, 8);
            schema.Rows.Add(PostgresQueryBuilder.IS_FALLBACK_TO_UPDATE, 1);

            Mock<DbDataReader> reader = new();
            reader.SetupGet(x => x.RecordsAffected).Returns(0);
            reader.SetupGet(x => x.HasRows).Returns(true);
            reader.SetupSequence(x => x.ReadAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false)
                .ReturnsAsync(false);
            reader.Setup(x => x.GetSchemaTable()).Returns(schema);
            reader.Setup(x => x.GetOrdinal(PostgresQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK)).Returns(0);
            reader.Setup(x => x.GetOrdinal(PostgresQueryBuilder.IS_FALLBACK_TO_UPDATE)).Returns(1);
            reader.Setup(x => x.IsDBNull(It.IsAny<int>())).Returns(false);
            reader.Setup(x => x[PostgresQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK]).Returns(0L);
            reader.Setup(x => x[PostgresQueryBuilder.IS_FALLBACK_TO_UPDATE]).Returns(true);
            reader.Setup(x => x.NextResultAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() =>
                executor.GetMultipleResultSetsIfAnyAsync(reader.Object));

            Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
        }

        private static PostgreSqlQueryExecutor CreateExecutor()
        {
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(
                    DatabaseType.PostgreSQL,
                    "Host=localhost;Database=test;Username=user;Password=password"),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            return new PostgreSqlQueryExecutor(
                configProvider,
                new PostgreSqlDbExceptionParser(configProvider),
                NullLogger<IQueryExecutor>.Instance,
                new HttpContextAccessor());
        }
    }
}
