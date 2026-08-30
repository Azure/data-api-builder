// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class MetadataProviderFactoryCoverageTests
    {
        [TestMethod]
        public void Constructor_CreatesProviderForEverySupportedSqlDatabaseType()
        {
            Dictionary<string, DataSource> dataSources = new()
            {
                ["mssql"] = new(DatabaseType.MSSQL, string.Empty),
                ["dwsql"] = new(DatabaseType.DWSQL, string.Empty),
                ["postgresql"] = new(DatabaseType.PostgreSQL, string.Empty),
                ["mysql"] = new(DatabaseType.MySQL, string.Empty)
            };

            MetadataProviderFactory factory = CreateFactory(dataSources);

            Assert.AreEqual(dataSources.Count, factory.ListMetadataProviders().Count());
            Assert.AreEqual(2, factory.ListMetadataProviders().OfType<MsSqlMetadataProvider>().Count());
            Assert.AreEqual(1, factory.ListMetadataProviders().OfType<PostgreSqlMetadataProvider>().Count());
            Assert.AreEqual(1, factory.ListMetadataProviders().OfType<MySqlMetadataProvider>().Count());
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

        private static MetadataProviderFactory CreateFactory(Dictionary<string, DataSource> dataSources)
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
            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem) { RuntimeConfig = config };
            RuntimeConfigProvider provider = new(loader);
            RuntimeConfigValidator validator = new(
                provider,
                fileSystem,
                Mock.Of<ILogger<RuntimeConfigValidator>>());

            return new MetadataProviderFactory(
                provider,
                validator,
                Mock.Of<IAbstractQueryManagerFactory>(),
                Mock.Of<ILogger<ISqlMetadataProvider>>(),
                fileSystem,
                handler: null,
                isValidateOnly: true);
        }
    }
}
