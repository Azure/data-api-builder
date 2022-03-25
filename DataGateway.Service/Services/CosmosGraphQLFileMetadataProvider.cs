using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Services.MetadataProviders;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// GraphQL Metadata Provider specific to Cosmos Db.
    /// </summary>
    public class CosmosGraphQLFileMetadataProvider : GraphQLFileMetadataProvider
    {
        public CosmosGraphQLFileMetadataProvider(
            IOptions<DataGatewayConfig> dataGatewayConfig)
            : base(dataGatewayConfig)
        {
        }
    }
}
