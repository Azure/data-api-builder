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
    /// <summary>
    /// Sql specific version of GraphQLFileMetadataProvider.
    /// Currently, also serves as the developer configuration file
    /// that specifies which tables from the database schema are to be exposed.
    /// This database schema is further enriched using the SqlMetadataProvider
    /// for the required tables.
    /// </summary>
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

        /// <summary>
        ///  Copy Constructor
        /// </summary>
        /// <param name="source">Source to copy from</param>
        public SqlGraphQLFileMetadataProvider(
                SqlGraphQLFileMetadataProvider source)
            : base(source)
        {
            _sqlMetadataProvider = source._sqlMetadataProvider;
        }

        /// Default Constructor for Mock tests.
        public SqlGraphQLFileMetadataProvider() : base()
        {
            _sqlMetadataProvider = new MsSqlMetadataProvider();
        }

        /// </inheritdoc>
        public override async Task InitializeAsync()
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            await EnrichDatabaseSchemaWithTableMetadata();
            InitFilterParser();
            timer.Stop();
            Console.WriteLine($"Done inferring Sql database schema in {timer.ElapsedMilliseconds}ms.");
        }

        /// <summary>
        /// Obtains the underlying TableDefinition for the given table from the DatabaseSchema.
        /// </summary>
        public virtual TableDefinition GetTableDefinition(string name)
        {
            if (!GraphQLResolverConfig.DatabaseSchema!.Tables.TryGetValue(name, out TableDefinition? metadata))
            {
                throw new KeyNotFoundException($"Table Definition for {name} does not exist.");
            }

            return metadata;
        }

        /// <summary>
        /// Enrich the database schema with the missing information
        /// from config file but the runtime still needs.
        /// </summary>
        private async Task EnrichDatabaseSchemaWithTableMetadata()
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

        private void InitFilterParser()
        {
            if (GraphQLResolverConfig == null || GraphQLResolverConfig.DatabaseSchema == null)
            {
                throw new DataGatewayException(
                    message: "Developer configuration file has not been initialized.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            ODataFilterParser.BuildModel(GraphQLResolverConfig.DatabaseSchema);
        }
    }
}
