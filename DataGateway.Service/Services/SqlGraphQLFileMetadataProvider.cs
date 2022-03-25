using System;
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
        private FilterParser? _filterParser;

        public SqlGraphQLFileMetadataProvider(
            IOptions<DataGatewayConfig> dataGatewayConfig)
            : base(dataGatewayConfig)
        {
        }

        /// Default Constructor for Mock tests.
        public GraphQLFileMetadataProvider()
        {
            GraphQLResolverConfig = new(string.Empty, string.Empty);
            _mutationResolvers = new();
            CloudDbType = DatabaseType.None;
        }

        /// <summary>
        /// Returns the Filter Parser
        /// </summary>
        public FilterParser FilterParser()
        {
            if (_filterParser == null)
            {
                throw new InvalidOperationException("No filter parser has been initialised");
            }

            return _filterParser;
        }

        public TableDefinition GetTableDefinition(string name)
        {
            if (!GraphQLResolverConfig.DatabaseSchema!.Tables.TryGetValue(name, out TableDefinition? metadata))
            {
                throw new KeyNotFoundException($"Table Definition for {name} does not exist.");
            }

            return metadata;
        }

        /// <summary>
        /// Initializes the filter parser using the database schema.
        /// </summary>
        public void InitFilterParser()
        {
            if (GraphQLResolverConfig == null || GraphQLResolverConfig.DatabaseSchema == null)
            {
                throw new DataGatewayException(
                    message: "Developer configuration file has not been initialized.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            _filterParser = new(GraphQLResolverConfig.DatabaseSchema);
        }
    }
}
