using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Azure.DataGateway.Service.Services
{
    public class SqlHostedService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        public SqlHostedService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

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
