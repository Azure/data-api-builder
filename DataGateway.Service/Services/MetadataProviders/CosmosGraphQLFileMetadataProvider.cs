using Azure.DataGateway.Service.Configurations;
using Microsoft.Extensions.Options;

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
