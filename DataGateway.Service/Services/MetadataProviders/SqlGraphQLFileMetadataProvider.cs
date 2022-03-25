using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Services
{
    public class SqlGraphQLFileMetadataProvider : GraphQLFileMetadataProvider
    {
        private readonly ISqlMetadataProvider _sqlMetadataProvider;

        public FilterParser ODataFilterParser { get; private set; } = new();

        public SqlGraphQLFileMetadataProvider(
            IOptions<DataGatewayConfig> dataGatewayConfig,
            ISqlMetadataProvider sqlMetadataProvider)
            : base(dataGatewayConfig)
        {
            _sqlMetadataProvider = sqlMetadataProvider;
        }

        public SqlGraphQLFileMetadataProvider(
            SqlGraphQLFileMetadataProvider source)
            : base(source)
        {
            _sqlMetadataProvider = source._sqlMetadataProvider;
        }

        /// Default Constructor for Mock tests.
        public SqlGraphQLFileMetadataProvider():base()
        {
            _sqlMetadataProvider = new MsSqlMetadataProvider();
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

        public virtual TableDefinition GetTableDefinition(string name)
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

            ODataFilterParser = new(GraphQLResolverConfig.DatabaseSchema);
        }
    }
}
