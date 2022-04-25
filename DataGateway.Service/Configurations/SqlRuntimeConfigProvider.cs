using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
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
    public class SqlRuntimeConfigProvider : RuntimeConfigProvider
    {
        private readonly ISqlMetadataProvider _sqlMetadataProvider;

        public FilterParser ODataFilterParser { get; private set; } = new();

        public SqlRuntimeConfigProvider(
            IOptions<DataGatewayConfig> dataGatewayConfig,
            ISqlMetadataProvider sqlMetadataProvider)
            : base(dataGatewayConfig)
        {
            _sqlMetadataProvider = sqlMetadataProvider;
        }

        /// </inheritdoc>
        public override async Task InitializeAsync()
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            await EnrichEntitiesWithTableMetadata();
            InitFilterParser();
            timer.Stop();
            Console.WriteLine($"Done inferring Sql database schema in {timer.ElapsedMilliseconds}ms.");
        }

        /// <summary>
        /// Obtains the underlying TableDefinition for the given entity name.
        /// </summary>
        public virtual TableDefinition GetTableDefinition(string name)
        {
            if (!RuntimeConfig.Entities.TryGetValue(name, out Entity? entity))
            {
                throw new KeyNotFoundException($"Table Definition for {name} does not exist.");
            }

            if (entity is not SqlEntity)
            {
                throw new InvalidCastException($"Table Definition for {name} cannot be obtained " +
                    $"since it is not backed by a relational database.");
            }

            return ((SqlEntity)entity).TableDefinition;
        }

        /// <summary>
        /// Enrich the entities in the runtime config with the
        /// table definition information needed by the runtime to serve requests.
        /// </summary>
        private async Task EnrichEntitiesWithTableMetadata()
        {
            if (RuntimeConfig == null)
            {
                throw new DataGatewayException(
                    message: "Developer configuration file has not been initialized.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            string schemaName = string.Empty;
            foreach (SqlEntity sqlEntity
                in RuntimeConfig.Entities.Values)
            {
                switch (CloudDbType)
                {
                    case DatabaseType.mssql:
                        schemaName = "dbo";
                        await _sqlMetadataProvider.PopulateTableDefinitionAsync(
                            schemaName,
                            sqlEntity.SourceName,
                            sqlEntity.TableDefinition);
                        break;
                    case DatabaseType.postgresql:
                        schemaName = "public";
                        await _sqlMetadataProvider.PopulateTableDefinitionAsync(
                            schemaName,
                            sqlEntity.SourceName,
                            sqlEntity.TableDefinition);
                        break;
                    case DatabaseType.mysql:
                        await _sqlMetadataProvider.PopulateTableDefinitionAsync(
                            schemaName,
                            sqlEntity.SourceName,
                            sqlEntity.TableDefinition);
                        break;
                    default:
                        throw new ArgumentException($"Enriching entities with table definition " +
                            $"for this database type: {CloudDbType} " +
                            $"is not supported.");
                }
            }

            await _sqlMetadataProvider.PopulateForeignKeyDefinitionAsync(
                schemaName,
                RuntimeConfig.Entities.Values);

        }

        private void InitFilterParser()
        {
            if (RuntimeConfig == null)
            {
                throw new DataGatewayException(
                    message: "Runtime configuration file has not been initialized.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            ODataFilterParser.BuildModel(RuntimeConfig.Entities);
        }
    }
}
