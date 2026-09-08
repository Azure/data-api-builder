// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Data.Common;
using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlDbExceptionParserHelperTests
    {
        [DataTestMethod]
        [DataRow("08006", true)]
        [DataRow("not-transient", false)]
        [DataRow(null, false)]
        public void IsTransientException_UsesPostgreSqlState(string? sqlState, bool expected)
        {
            PostgreSqlDbExceptionParser parser = CreateParser();
            Mock<DbException> exception = new();
            exception.SetupGet(x => x.SqlState).Returns(sqlState);

            Assert.AreEqual(expected, parser.IsTransientException(exception.Object));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("unknown")]
        public void GetHttpStatusCodeForException_UnrecognizedStateReturnsInternalServerError(string? sqlState)
        {
            PostgreSqlDbExceptionParser parser = CreateParser();
            Mock<DbException> exception = new();
            exception.SetupGet(x => x.SqlState).Returns(sqlState);

            Assert.AreEqual(HttpStatusCode.InternalServerError, parser.GetHttpStatusCodeForException(exception.Object));
        }

        private static PostgreSqlDbExceptionParser CreateParser()
        {
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.PostgreSQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            return new PostgreSqlDbExceptionParser(configProvider);
        }
    }
}
