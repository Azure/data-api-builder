using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
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
        private readonly ISqlMetadataProvider _sqlMetadataProvider;

        public SqlGraphQLFileMetadataProvider(
            IOptions<DataGatewayConfig> dataGatewayConfig,
            ISqlMetadataProvider sqlMetadataProvider)
            : base(dataGatewayConfig)
        {
            _sqlMetadataProvider = sqlMetadataProvider;
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

        /// <summary>
        /// Enrich the database schema with the missing information
        /// from file but the runtime still needs.
        /// </summary>
        public async Task EnrichDatabaseSchemaWithTableMetadata()
        {
            if (GraphQLResolverConfig == null || GraphQLResolverConfig.DatabaseSchema == null)
            {
                throw new DataGatewayException(
                    message: "Developer configuration file has not been initialized.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            string schemaName = string.Empty;
            foreach ((string tableName, TableDefinition tableDefinition) in GraphQLResolverConfig.DatabaseSchema.Tables)
            {
                switch (CloudDbType)
                {
                    case DatabaseType.MsSql:
                        schemaName = "dbo";
                        await _sqlMetadataProvider!.PopulateTableDefinitionAsync(schemaName, tableName, tableDefinition);
                        break;
                    case DatabaseType.PostgreSql:
                        schemaName = "public";
                        await _sqlMetadataProvider!.PopulateTableDefinitionAsync(schemaName, tableName, tableDefinition);
                        break;
                    case DatabaseType.MySql:
                        await _sqlMetadataProvider!.PopulateTableDefinitionAsync(schemaName, tableName, tableDefinition);
                        break;
                    default:
                        throw new ArgumentException($"Enriching database schema " +
                            $"for this database type: {CloudDbType} " +
                            $"is not supported.");
                }
            }
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
