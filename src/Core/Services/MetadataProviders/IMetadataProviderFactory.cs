// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.DatabasePrimitives;

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

        public List<Exception> GetAllMetadataExceptions();

        /// <summary>
        /// Initializes the metadata providers.
        /// </summary>
        public Task InitializeAsync();

        /// <summary>
        /// Initializes the metadata providers with parameters
        /// currently this is used by GraphQL workload
        /// </summary>
        public void InitializeAsync(Dictionary<string, Dictionary<string,DatabaseObject>> EntityToDatabaseObjectMap,
            Dictionary<string, Dictionary<string,string>> graphQLStoredProcedureExposedNameToEntityNameMap);
    }
}
