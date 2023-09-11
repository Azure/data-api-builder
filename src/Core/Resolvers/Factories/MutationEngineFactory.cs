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

namespace Azure.DataApiBuilder.Core.Resolvers.Factories
{
    /// <summary>
    /// MutationEngineFactory class.
    /// Used to get the IMutationEngine based on database type.
    /// </summary>
    public class MutationEngineFactory : IMutationEngineFactory
    {
        private readonly IEnumerable<IMutationEngine> _mutationEngines;

        /// <summary>
        /// Initializes a new instance of the <see cref="MutationEngineFactory"/> class.
        /// </summary>
        /// <param name="runtimeConfigProvider">runtimeConfigProvider.</param>
        /// <param name="queryManagerFactory">queryManagerFactory</param>
        /// <param name="metadataProviderFactory">metadataProviderFactory.</param>
        /// <param name="cosmosClientProvider">cosmosClientProvider</param>
        /// <param name="queryEngineFactory">queryEngineFactory.</param>
        /// <param name="httpContextAccessor">httpContextAccessor.</param>
        /// <param name="authorizationResolver">authorizationResolver.</param>
        /// <param name="gQLFilterParser">GqlFilterParser.</param>
        public MutationEngineFactory(RuntimeConfigProvider runtimeConfigProvider,
            IQueryManagerFactory queryManagerFactory,
            IMetadataProviderFactory metadataProviderFactory,
            CosmosClientProvider cosmosClientProvider,
            IQueryEngineFactory queryEngineFactory,
            IHttpContextAccessor httpContextAccessor,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser)
        {
            _mutationEngines = new List<IMutationEngine>();

            bool sqlEngineNeeded = runtimeConfigProvider.GetConfig().GetDataSourceNamesToDataSourcesIterator().Any
                (x => x.Value.DatabaseType == DatabaseType.MSSQL || x.Value.DatabaseType == DatabaseType.PostgreSQL || x.Value.DatabaseType == DatabaseType.MySQL);
            if (sqlEngineNeeded)
            {
                _mutationEngines = _mutationEngines.Append(new SqlMutationEngine(queryManagerFactory, metadataProviderFactory, queryEngineFactory, authorizationResolver, gQLFilterParser, httpContextAccessor, runtimeConfigProvider));
            }

            bool cosmosEngineNeeded = runtimeConfigProvider.GetConfig().GetDataSourceNamesToDataSourcesIterator().Any
                (x => x.Value.DatabaseType == DatabaseType.CosmosDB_NoSQL || x.Value.DatabaseType == DatabaseType.CosmosDB_PostgreSQL);

            if (cosmosEngineNeeded)
            {
                _mutationEngines = _mutationEngines.Append(new CosmosMutationEngine(cosmosClientProvider, metadataProviderFactory, authorizationResolver, runtimeConfigProvider));
            }
        }

        /// <summary>
        /// Gets the MutationEngine based on database type.
        /// </summary>
        /// <param name="databaseType">databaseType.</param>
        /// <returns>IMutationEngine.</returns>
        /// <exception cref="DataApiBuilderException">Exception if databaseType not found.</exception>
        public IMutationEngine GetMutationEngine(DatabaseType databaseType)
        {
            IMutationEngine mutationEngine = databaseType switch
            {
                DatabaseType.CosmosDB_NoSQL or DatabaseType.CosmosDB_PostgreSQL => _mutationEngines.First(engine => engine.GetType() == typeof(CosmosMutationEngine)),
                DatabaseType.MySQL or DatabaseType.MSSQL or DatabaseType.PostgreSQL => _mutationEngines.First(engine => engine.GetType() == typeof(SqlMutationEngine)),
                _ => throw new DataApiBuilderException($"{nameof(databaseType)}:{databaseType} could not be found within the config", HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.DataSourceNotFound)
            };

            return mutationEngine;
        }
    }
}
