using System.Collections.Generic;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services.MetadataProviders;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Services
{
    public class SqlGraphQLFileMetadataProvider : GraphQLFileMetadataProvider
    {
        public FilterParser FilterParser { get; init; }

        public SqlGraphQLFileMetadataProvider(
            IOptions<DataGatewayConfig> dataGatewayConfig)
            : base(dataGatewayConfig)
        {
            // Since this is the Sql File Metadata Provider -
            // this is expected to have a non-null DatabaseSchema
            FilterParser = new(GraphQLResolverConfig.DatabaseSchema!);
        }

        public TableDefinition GetTableDefinition(string name)
        {
            if (!GraphQLResolverConfig.DatabaseSchema!.Tables.TryGetValue(name, out TableDefinition? metadata))
            {
                throw new KeyNotFoundException($"Table Definition for {name} does not exist.");
            }

            return metadata;
        }
    }
}
