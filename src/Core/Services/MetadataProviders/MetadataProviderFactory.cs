// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders
{
    /// <inheritdoc />
    public class MetadataProviderFactory : IMetadataProviderFactory
    {
        private readonly IDictionary<string, ISqlMetadataProvider> _metadataProviders;

        public MetadataProviderFactory(IEnumerable<ISqlMetadataProvider> metadataProviders)
        {
            _metadataProviders = metadataProviders.ToDictionary(provider => provider.GetDatabaseSourceName(), provider => provider);
        }

        /// <inheritdoc />
        public ISqlMetadataProvider GetMetadataProvider(string dataSourceName)
        {
            if (!_metadataProviders.ContainsKey(dataSourceName))
            {
                throw new DataApiBuilderException($"{nameof(dataSourceName)}:{dataSourceName} could not be found within the config", HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
            }

            return _metadataProviders[dataSourceName];
        }

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            foreach ((_, ISqlMetadataProvider provider) in _metadataProviders)
            {
                await provider.InitializeAsync();
            }
        }
    }
}
