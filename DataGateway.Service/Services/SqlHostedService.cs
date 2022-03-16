using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// Use SqlHostedService to gather metadata information
    /// asynchronously from Sql-like databases.
    /// </summary>
    public class SqlHostedService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        public SqlHostedService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// As soon as this service is created, this task is started before
        /// the app can serve requests.
        /// The need for this is to have the metadata information gathered from the database
        /// before requests can be served.
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Gets the FileMetadataStoreProvider instance
            FileMetadataStoreProvider metadataStoreProvider =
                (FileMetadataStoreProvider)_serviceProvider.GetRequiredService<IMetadataStoreProvider>();

            // Enriches the database schema asynchronously
            await metadataStoreProvider.EnrichDatabaseSchemaWithTableMetadata();
            metadataStoreProvider.InitFilterParser();
        }

        // noop
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
