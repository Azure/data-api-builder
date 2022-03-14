using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Azure.DataGateway.Service.Services
{
    public class PhoenixMetadataStoreProvider : BaseMetadataProvider
    {
        public PhoenixMetadataStoreProvider(IOptionsMonitor<DataGatewayConfig> dataGatewayConfig)
            : base(dataGatewayConfig.CurrentValue.ResolverConfig, dataGatewayConfig.CurrentValue.DatabaseType, dataGatewayConfig.CurrentValue.DatabaseConnection.ConnectionString, dataGatewayConfig.CurrentValue.GraphQLSchema)
        {

        }
    }

    /// <summary>
    /// Reads GraphQL Schema and resolver config from text files to make available to GraphQL service.
    /// </summary>
    public class FileMetadataStoreProvider : BaseMetadataProvider
    {
        public FileMetadataStoreProvider(IOptionsMonitor<DataGatewayConfig> dataGatewayConfig)
        : this(dataGatewayConfig.CurrentValue.ResolverConfigFile,
              dataGatewayConfig.CurrentValue.DatabaseType,
              dataGatewayConfig.CurrentValue.DatabaseConnection.ConnectionString)
        {
            dataGatewayConfig.OnChange((newValue) =>
            {
                // TOOD: this(newValue);
            });
        }

        public FileMetadataStoreProvider(
            string resolverConfigPath,
            DatabaseType databaseType,
            string connectionString) :
            base(File.ReadAllText(resolverConfigPath), databaseType, connectionString)
        {
        }
    }
}
