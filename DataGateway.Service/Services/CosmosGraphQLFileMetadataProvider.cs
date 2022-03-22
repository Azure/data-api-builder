using Azure.DataGateway.Service.Configurations;
using Microsoft.Extensions.Options;
using Azure.DataGateway.Service.Services.MetadataProviders;

namespace Azure.DataGateway.Service.Services
{

    public class CosmosGraphQLFileMetadataProvider : GraphQLFileMetadataProvider
    {
        public CosmosGraphQLFileMetadataProvider(
            IOptions<DataGatewayConfig> dataGatewayConfig)
            : base(dataGatewayConfig)
        {
        }

    }
}
