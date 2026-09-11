// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class QueryManagerFactoryCoverageTests
    {
        [TestMethod]
        public void Constructor_CreatesManagersForEverySupportedSqlDatabaseType()
        {
            Dictionary<string, DataSource> dataSources = new()
            {
                ["mssql"] = new(DatabaseType.MSSQL, string.Empty),
                ["mssql-duplicate"] = new(DatabaseType.MSSQL, string.Empty),
                ["dwsql"] = new(DatabaseType.DWSQL, string.Empty),
                ["postgresql"] = new(DatabaseType.PostgreSQL, string.Empty),
                ["mysql"] = new(DatabaseType.MySQL, string.Empty),
                ["cosmos"] = new(DatabaseType.CosmosDB_NoSQL, string.Empty)
            };

            QueryManagerFactory factory = CreateFactory(dataSources);

            Assert.IsInstanceOfType<MsSqlQueryBuilder>(factory.GetQueryBuilder(DatabaseType.MSSQL));
            Assert.IsInstanceOfType<DwSqlQueryBuilder>(factory.GetQueryBuilder(DatabaseType.DWSQL));
            Assert.IsInstanceOfType<PostgresQueryBuilder>(factory.GetQueryBuilder(DatabaseType.PostgreSQL));
            Assert.IsInstanceOfType<MySqlQueryBuilder>(factory.GetQueryBuilder(DatabaseType.MySQL));
            Assert.IsInstanceOfType<MsSqlQueryExecutor>(factory.GetQueryExecutor(DatabaseType.MSSQL));
            Assert.IsInstanceOfType<MsSqlDbExceptionParser>(factory.GetDbExceptionParser(DatabaseType.MSSQL));
        }

        [TestMethod]
        public void Accessors_RejectUnconfiguredDatabaseType()
        {
            QueryManagerFactory factory = CreateFactory(new Dictionary<string, DataSource>
            {
                ["mssql"] = new(DatabaseType.MSSQL, string.Empty)
            });
            DatabaseType missing = (DatabaseType)998;

            Assert.ThrowsException<DataApiBuilderException>(() => factory.GetQueryBuilder(missing));
            Assert.ThrowsException<DataApiBuilderException>(() => factory.GetQueryExecutor(missing));
            Assert.ThrowsException<DataApiBuilderException>(() => factory.GetDbExceptionParser(missing));
        }

        [TestMethod]
        public void Constructor_RejectsUnsupportedDatabaseType()
        {
            Dictionary<string, DataSource> dataSources = new()
            {
                ["unsupported"] = new((DatabaseType)999, string.Empty)
            };

            Assert.ThrowsException<NotSupportedException>(() => CreateFactory(dataSources));
        }

        private static QueryManagerFactory CreateFactory(Dictionary<string, DataSource> dataSources)
        {
            DataSource defaultDataSource = dataSources.First().Value;
            RuntimeConfig config = new(
                Schema: string.Empty,
                DataSource: defaultDataSource,
                Runtime: new RuntimeOptions(null, null, null, null),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
                DefaultDataSourceName: dataSources.First().Key,
                DataSourceNameToDataSource: dataSources,
                EntityNameToDataSourceName: new Dictionary<string, string>());
            FileSystemRuntimeConfigLoader loader = new(new MockFileSystem()) { RuntimeConfig = config };
            RuntimeConfigProvider provider = new(loader);

            return new QueryManagerFactory(
                provider,
                Mock.Of<ILogger<IQueryExecutor>>(),
                new HttpContextAccessor(),
                handler: null);
        }
    }
}
