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
        /// <param name="dataSourceName">dataSourceName.</param>
        /// <returns>ISqlMetadataProvider.</returns>
        public ISqlMetadataProvider GetMetadataProvider(string dataSourceName);

        /// <summary>
        /// Initializes the metadata providers.
        /// </summary>
        /// <returns>Task.</returns>
        public Task InitializeAsync();
    }
}
