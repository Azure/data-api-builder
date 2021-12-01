using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Configurations;

namespace Azure.DataGateway.Service.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public sealed class BindController : ControllerBase
    {
        InMemoryUpdateableConfigurationProvider _metadataStoreProvider;
        public BindController(InMemoryUpdateableConfigurationProvider metadataStoreProvider)
        {
            if (metadataStoreProvider == null)
            {
                throw new ArgumentNullException(nameof(metadataStoreProvider), "No metadata store provider was provided");
            }

            _metadataStoreProvider = metadataStoreProvider;
        }

        [HttpPost]
        public async Task<BindResponse> BindAsync(
            [FromBody] BindRequest bindRequest,
            [FromHeader(Name = "Authorization")] string aadToken)
        {
            //var watch = System.Diagnostics.Stopwatch.StartNew();
            //System.Diagnostics.Debug.WriteLine($"Bind Start : {watch.ElapsedMilliseconds}ms");

            CosmosDbConfiguration cosmosDbConfiguration = CosmosDbConfiguration.Instance;
            if (cosmosDbConfiguration.CosmosEndpoint != null)
            {
                throw new Exception("Container is already bound.");
            }

            await cosmosDbConfiguration.InitializeAsync(bindRequest, aadToken);

            string connectionString = $"AccountEndpoint={cosmosDbConfiguration.CosmosEndpoint};AccountKey={cosmosDbConfiguration.CosmosKey}";
            Dictionary<string, string> properties = new()
            {
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", connectionString },
                { "DataGatewayConfig:GraphQLSchema", bindRequest.GraphQLSchema },
                { "DataGatewayConfig:ResolverConfig", bindRequest.DatabaseConfig },
                { "DataGatewayConfig:DatabaseType", "Cosmos" }
            };

            _metadataStoreProvider.SetManyAndReload(properties);

            ContainerMetadata.Instance.AllocatedTime = DateTime.UtcNow;
            ContainerMetadata.Instance.SessionToken = bindRequest.SessionToken;
            ContainerMetadata.Instance.LastHeartbeatTime = DateTime.UtcNow;

            //System.Diagnostics.Debug.WriteLine($"Bind Stop : {watch.ElapsedMilliseconds}ms");
            //Response.Headers.Add("ServerTiming", $"01-InitializeCosmosDbConfig; dur={watch.ElapsedMilliseconds}");
            return new BindResponse { SessionToken = ContainerMetadata.Instance.SessionToken, AllocationTime = ContainerMetadata.Instance.AllocatedTime };
        }
    }
}
