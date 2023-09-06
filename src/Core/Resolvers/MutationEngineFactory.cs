// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Core.Resolvers
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
        /// <param name="mutationEngines">mutationEngines from DI container</param>
        public MutationEngineFactory(IEnumerable<IMutationEngine> mutationEngines)
        {
            _mutationEngines = mutationEngines;
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
