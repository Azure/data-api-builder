// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders
{
    /// <summary>
    /// IMetadataProviderFactory class.
    /// Used to get the appropriate metadata provider based on the data source name.
    /// </summary>
    public interface IMetadataProviderFactory
    {
        /// <summary>
        /// Gets the appropriate metadata provider based on the data source name.
        /// </summary>
        public ISqlMetadataProvider GetMetadataProvider(string dataSourceName);

        /// <summary>
        /// Lists the metadata providers.
        /// </summary>
        public IEnumerable<ISqlMetadataProvider> ListMetadataProviders();

        /// <summary>
        /// Initializes the metadata providers.
        /// </summary>
        public Task InitializeAsync();
    }
}
