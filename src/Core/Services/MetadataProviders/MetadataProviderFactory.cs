// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using System.Net;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting.Logging;

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders
{
    /// <inheritdoc />
    public class MetadataProviderFactory : IMetadataProviderFactory
    {
        private readonly IDictionary<string, ISqlMetadataProvider> _metadataProviders;

        public MetadataProviderFactory(
            RuntimeConfigProvider runtimeConfigProvider,
            IAbstractQueryManagerFactory queryManagerFactory,
            ILogger<ISqlMetadataProvider> logger,
            IFileSystem fileSystem,
            HotReloadEventHandler<CustomEventArgs> handler,
            bool isValidateOnly = false)
        {
            handler.MetadataProvider_Subscribe(MetadataProviderFactory_ConfigChangeEventReceived);
            _metadataProviders = new Dictionary<string, ISqlMetadataProvider>();
            foreach ((string dataSourceName, DataSource dataSource) in runtimeConfigProvider.GetConfig().GetDataSourceNamesToDataSourcesIterator())
            {
                ISqlMetadataProvider metadataProvider = dataSource.DatabaseType switch
                {
                    DatabaseType.CosmosDB_NoSQL => new CosmosSqlMetadataProvider(runtimeConfigProvider, fileSystem),
                    DatabaseType.MSSQL => new MsSqlMetadataProvider(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName, isValidateOnly),
                    DatabaseType.DWSQL => new MsSqlMetadataProvider(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName, isValidateOnly),
                    DatabaseType.PostgreSQL => new PostgreSqlMetadataProvider(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName, isValidateOnly),
                    DatabaseType.MySQL => new MySqlMetadataProvider(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName, isValidateOnly),
                    _ => throw new NotSupportedException(dataSource.DatabaseTypeNotSupportedMessage),
                };

                _metadataProviders.Add(dataSourceName, metadataProvider);
            }
        }

        private void AddMetadataProviders(IEnumerable<KeyValuePair<string, DataSource>> iterator)
        {
            foreach ((string dataSourceName, DataSource dataSource) in iterator)
            {
                ISqlMetadataProvider metadataProvider = dataSource.DatabaseType switch
                {
                    DatabaseType.CosmosDB_NoSQL => new CosmosSqlMetadataProvider(runtimeConfigProvider, fileSystem),
                    DatabaseType.MSSQL => new MsSqlMetadataProvider(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName, isValidateOnly),
                    DatabaseType.DWSQL => new MsSqlMetadataProvider(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName, isValidateOnly),
                    DatabaseType.PostgreSQL => new PostgreSqlMetadataProvider(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName, isValidateOnly),
                    DatabaseType.MySQL => new MySqlMetadataProvider(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName, isValidateOnly),
                    _ => throw new NotSupportedException(dataSource.DatabaseTypeNotSupportedMessage),
                };

                _metadataProviders.Add(dataSourceName, metadataProvider);
            }
        }

        public void MetadataProviderFactory_ConfigChangeEventReceived(object? sender, CustomEventArgs args)
        {

        }

        

        /// <inheritdoc />
        public ISqlMetadataProvider GetMetadataProvider(string dataSourceName)
        {
            if (!(_metadataProviders.TryGetValue(dataSourceName, out ISqlMetadataProvider? metadataProvider)))
            {
                throw new DataApiBuilderException(
                    $"{nameof(dataSourceName)}:{dataSourceName} could not be found within the config",
                    HttpStatusCode.BadRequest,
                    DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
            }

            return metadataProvider;
        }

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            foreach ((_, ISqlMetadataProvider provider) in _metadataProviders)
            {
                if (provider is not null)
                {
                    await provider.InitializeAsync();
                }
            }
        }

        /// <summary>
        /// Captures all the metadata exceptions from all the metadata providers at a single place.
        /// </summary>
        /// <returns>List of Exceptions</returns>
        public List<Exception> GetAllMetadataExceptions()
        {
            List<Exception> allMetadataExceptions = new();
            foreach ((_, ISqlMetadataProvider provider) in _metadataProviders)
            {
                if (provider is not null)
                {
                    allMetadataExceptions.AddRange(provider.SqlMetadataExceptions);
                }
            }

            return allMetadataExceptions;
        }

        public IEnumerable<ISqlMetadataProvider> ListMetadataProviders()
        {
            return _metadataProviders.Values;
        }

        /// <inheritdoc />
        public void InitializeAsync(
            Dictionary<string, Dictionary<string, DatabaseObject>> EntityToDatabaseObjectMap,
            Dictionary<string, Dictionary<string, string>> graphQLStoredProcedureExposedNameToEntityNameMap)
        {
            foreach ((string dataSourceName, ISqlMetadataProvider provider) in _metadataProviders)
            {
                if (provider is not null)
                {
                    provider.InitializeAsync(EntityToDatabaseObjectMap[dataSourceName], graphQLStoredProcedureExposedNameToEntityNameMap[dataSourceName]);
                }
            }
        }
    }
}
