// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Resolvers.Factories
{
    /// <summary>
    /// QueryEngineFactory class.
    /// Used to get the appropriate queryEngine based on database type.
    /// </summary>
    public class QueryEngineFactory : IQueryEngineFactory
    {
        private readonly IEnumerable<IQueryEngine> _queryEngines;

        /// <inheritdoc/>
        public QueryEngineFactory(RuntimeConfigProvider runtimeConfigProvider,
            IAbstractQueryManagerFactory queryManagerFactory,
            IMetadataProviderFactory metadataProviderFactory,
            CosmosClientProvider cosmosClientProvider,
            IHttpContextAccessor contextAccessor,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            ILogger<IQueryEngine> logger)
        {
            _queryEngines = new List<IQueryEngine>();

            IEnumerable<DataSource> dataSources = runtimeConfigProvider.GetConfig().ListAllDataSources();

            bool sqlEngineNeeded = dataSources.Any
                (x => x.DatabaseType == DatabaseType.MSSQL || x.DatabaseType == DatabaseType.PostgreSQL || x.DatabaseType == DatabaseType.MySQL);

            if (sqlEngineNeeded)
            {
                _queryEngines = _queryEngines.Append(new SqlQueryEngine(queryManagerFactory, metadataProviderFactory, contextAccessor, authorizationResolver, gQLFilterParser, logger, runtimeConfigProvider));
            }

            bool cosmosEngineNeeded = dataSources.Any
                (x => x.DatabaseType == DatabaseType.CosmosDB_NoSQL);

            if (cosmosEngineNeeded)
            {
                _queryEngines = _queryEngines.Append(new CosmosQueryEngine(cosmosClientProvider, metadataProviderFactory, authorizationResolver, gQLFilterParser));
            }

        }

        /// <inheritdoc/>
        public IQueryEngine GetQueryEngine(DatabaseType databaseType)
        {
            IQueryEngine queryEngine = databaseType switch
            {
                DatabaseType.CosmosDB_NoSQL => _queryEngines.First(engine => engine.GetType() == typeof(CosmosQueryEngine)),
                DatabaseType.MySQL or DatabaseType.MSSQL or DatabaseType.PostgreSQL => _queryEngines.First(engine => engine.GetType() == typeof(SqlQueryEngine)),
                _ => throw new DataApiBuilderException(
                    $"{nameof(databaseType)}:{databaseType} could not be found within the config",
                    HttpStatusCode.BadRequest,
                    DataApiBuilderException.SubStatusCodes.DataSourceNotFound)
            };

            return queryEngine;
        }
    }
}
