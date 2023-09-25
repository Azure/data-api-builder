// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders
{
    /// <inheritdoc />
    public class MetadataProviderFactory : IMetadataProviderFactory
    {
        private readonly IDictionary<string, ISqlMetadataProvider> _metadataProviders;

        public MetadataProviderFactory(RuntimeConfigProvider runtimeConfigProvider, IAbstractQueryManagerFactory queryManagerFactory, ILogger<ISqlMetadataProvider> logger, IFileSystem fileSystem)
        {
            _metadataProviders = new Dictionary<string, ISqlMetadataProvider>();
            foreach ((string dataSourceName, DataSource dataSource) in runtimeConfigProvider.GetConfig().GetDataSourceNamesToDataSourcesIterator())
            {
                ISqlMetadataProvider metadataProvider = dataSource.DatabaseType switch
                {
                    DatabaseType.CosmosDB_NoSQL => new CosmosSqlMetadataProvider(runtimeConfigProvider, fileSystem),
                    DatabaseType.MSSQL => new MsSqlMetadataProvider(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName),
                    DatabaseType.PostgreSQL => new PostgreSqlMetadataProvider(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName),
                    DatabaseType.MySQL => new MySqlMetadataProvider(runtimeConfigProvider, queryManagerFactory, logger, dataSourceName),
                    _ => throw new NotSupportedException(dataSource.DatabaseTypeNotSupportedMessage),
                };

                _metadataProviders.Add(dataSourceName, metadataProvider);
            }
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

        public IEnumerable<ISqlMetadataProvider> ListMetadataProviders()
        {
            return _metadataProviders.Values;
        }
    }
}
