// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Core.Resolvers
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
        /// <param name="queryEngines">queryEngines.</param>
        public QueryEngineFactory(IEnumerable<IQueryEngine> queryEngines)
        {
            _queryEngines = queryEngines;
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
