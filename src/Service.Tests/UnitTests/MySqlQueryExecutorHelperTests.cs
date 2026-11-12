// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MySqlConnector;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlQueryExecutorHelperTests
    {
        [TestMethod]
        public async Task SetManagedIdentityAccessToken_UnavailableDefaultCredentialIsIgnored()
        {
            const string connectionString = "Server=localhost;Database=test;User ID=user;";
            MySqlQueryExecutor executor = CreateExecutor(connectionString);
            Mock<DefaultAzureCredential> credential = new();
            credential
                .Setup(x => x.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromException<AccessToken>(new CredentialUnavailableException("Credential unavailable.")));
            executor.AzureCredential = credential.Object;
            using MySqlConnection connection = new(connectionString);

            await executor.SetManagedIdentityAccessTokenIfAnyAsync(connection, string.Empty);

            Assert.AreEqual(string.Empty, new MySqlConnectionStringBuilder(connection.ConnectionString).Password);
        }

        [TestMethod]
        public async Task GetMultipleResultSets_MissingExistenceMetadataThrowsInternalServerError()
        {
            MySqlQueryExecutor executor = CreateExecutor();
            Mock<DbDataReader> reader = new();
            reader.Setup(x => x.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() =>
                executor.GetMultipleResultSetsIfAnyAsync(reader.Object));

            Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
        }

        [TestMethod]
        public async Task GetMultipleResultSets_UpdateOnlyWithoutArgumentsThrowsInternalServerError()
        {
            MySqlQueryExecutor executor = CreateExecutor();
            Mock<DbDataReader> reader = CreateInsertPathReader(includeInsertResultSet: false);

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() =>
                executor.GetMultipleResultSetsIfAnyAsync(reader.Object));

            Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
        }

        [TestMethod]
        public async Task GetMultipleResultSets_EmptyInsertResultThrowsInternalServerError()
        {
            MySqlQueryExecutor executor = CreateExecutor();
            Mock<DbDataReader> reader = CreateInsertPathReader(includeInsertResultSet: true);

            DataApiBuilderException exception = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(() =>
                executor.GetMultipleResultSetsIfAnyAsync(reader.Object));

            Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
        }

        private static Mock<DbDataReader> CreateInsertPathReader(bool includeInsertResultSet)
        {
            DataTable schema = new();
            schema.Columns.Add("ColumnName", typeof(string));
            schema.Columns.Add("ColumnSize", typeof(int));
            schema.Rows.Add(MySqlQueryBuilder.ROW_EXISTED_BEFORE_UPSERT, 4);

            Mock<DbDataReader> reader = new();
            reader.SetupGet(x => x.RecordsAffected).Returns(0);
            reader.SetupGet(x => x.HasRows).Returns(true);
            reader.SetupSequence(x => x.ReadAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false)
                .ReturnsAsync(false)
                .ReturnsAsync(false);
            reader.Setup(x => x.GetSchemaTable()).Returns(schema);
            reader.Setup(x => x.GetOrdinal(MySqlQueryBuilder.ROW_EXISTED_BEFORE_UPSERT)).Returns(0);
            reader.Setup(x => x.IsDBNull(0)).Returns(false);
            reader.Setup(x => x[MySqlQueryBuilder.ROW_EXISTED_BEFORE_UPSERT]).Returns(0);
            reader.SetupSequence(x => x.NextResultAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(includeInsertResultSet);
            return reader;
        }

        private static MySqlQueryExecutor CreateExecutor(
            string connectionString = "Server=localhost;Database=test;User ID=user;Password=password;")
        {
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MySQL, connectionString),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            return new MySqlQueryExecutor(
                configProvider,
                new MySqlDbExceptionParser(configProvider),
                NullLogger<IQueryExecutor>.Instance,
                new HttpContextAccessor());
        }
    }
}
