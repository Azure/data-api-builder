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

        /// <summary>
        /// Initializes a new instance of the <see cref="QueryEngineFactory"/> class.
        /// </summary>
        /// <param name="runtimeConfigProvider">runtimeConfigProvider</param>
        /// <param name="queryManagerFactory">queryManagerFactory</param>
        /// <param name="metadataProviderFactory">metadataProviderFactory</param>
        /// <param name="cosmosClientProvider">cosmsClientProvider.</param>
        /// <param name="contextAccessor">HttpContextAccessor</param>
        /// <param name="authorizationResolver">AuthorizationResolver.</param>
        /// <param name="gQLFilterParser">gQLFilterParser</param>
        /// <param name="logger">logger</param>
        public QueryEngineFactory(RuntimeConfigProvider runtimeConfigProvider,
            IQueryManagerFactory queryManagerFactory,
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
                (x => x.DatabaseType == DatabaseType.CosmosDB_NoSQL || x.DatabaseType == DatabaseType.CosmosDB_PostgreSQL);

            if (cosmosEngineNeeded)
            {
                _queryEngines = _queryEngines.Append(new CosmosQueryEngine(cosmosClientProvider, metadataProviderFactory, authorizationResolver, gQLFilterParser));
            }

        }

        /// <summary>
        /// Gets the QueryEngine based on database type.
        /// </summary>
        /// <param name="databaseType">databaseType.</param>
        /// <returns>IQueryEngine</returns>
        /// <exception cref="DataApiBuilderException">exception thrown if databaseType not found.</exception>
        public IQueryEngine GetQueryEngine(DatabaseType databaseType)
        {
            IQueryEngine queryEngine = databaseType switch
            {
                DatabaseType.CosmosDB_NoSQL or DatabaseType.CosmosDB_PostgreSQL => _queryEngines.First(engine => engine.GetType() == typeof(CosmosQueryEngine)),
                DatabaseType.MySQL or DatabaseType.MSSQL or DatabaseType.PostgreSQL => _queryEngines.First(engine => engine.GetType() == typeof(SqlQueryEngine)),
                _ => throw new DataApiBuilderException($"{nameof(databaseType)}:{databaseType} could not be found within the config", HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.DataSourceNotFound)
            };

            return queryEngine;
        }
    }
}
