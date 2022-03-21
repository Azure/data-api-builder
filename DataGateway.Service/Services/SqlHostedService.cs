using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// Use SqlHostedService to gather metadata information
    /// asynchronously from Sql-like databases.
    /// </summary>
    public class SqlHostedService : IHostedService
    {
        private readonly IMetadataStoreProvider _fileMetadataProvider;
        public SqlHostedService(IMetadataStoreProvider fileMetadataProvider)
        {
            _fileMetadataProvider = fileMetadataProvider;
        }

        /// <summary>
        /// As soon as this service is created, this task is started before
        /// the app can serve requests.
        /// The need for this is to have the metadata information gathered from the database
        /// before requests can be served.
        /// Configure is not called unless StartAsync completes.
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Enriches the database schema asynchronously
            await _fileMetadataProvider.EnrichDatabaseSchemaWithTableMetadata();
            _fileMetadataProvider.InitFilterParser();
        }

        // noop
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
