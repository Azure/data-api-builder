using System.Collections.Generic;
using System.Net;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services.MetadataProviders;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Services
{
    public class SqlGraphQLFileMetadataProvider : GraphQLFileMetadataProvider
    {
        public FilterParser FilterParser { get; private set; } = new();

        public SqlGraphQLFileMetadataProvider(
            IOptions<DataGatewayConfig> dataGatewayConfig)
            : base(dataGatewayConfig)
        {
        }

        public TableDefinition GetTableDefinition(string name)
        {
            if (!GraphQLResolverConfig.DatabaseSchema!.Tables.TryGetValue(name, out TableDefinition? metadata))
            {
                throw new KeyNotFoundException($"Table Definition for {name} does not exist.");
            }

            return metadata;
        }

        public void InitFilterParser()
        {
            if (GraphQLResolverConfig == null || GraphQLResolverConfig.DatabaseSchema == null)
            {
                throw new DataGatewayException(
                    message: "Developer configuration file has not been initialized.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            FilterParser = new(GraphQLResolverConfig.DatabaseSchema);
        }
    }
}
