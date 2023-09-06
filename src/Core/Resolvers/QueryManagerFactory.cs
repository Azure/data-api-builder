// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// QueryManagerFactory interface.
    /// Used to get the appropriate query builder, query executor and exception parser and  based on the database type.
    /// </summary>
    public class QueryManagerFactory : IQueryManagerFactory
    {
        private readonly IDictionary<DatabaseType, IQueryBuilder> _queryBuilders;
        private readonly IDictionary<DatabaseType, IQueryExecutor> _queryExecutors;
        private readonly IDictionary<DatabaseType, DbExceptionParser> _dbExceptionsParsers;

        /// <summary>
        /// Initiates an instance of QueryManagerFactory
        /// </summary>
        /// <param name="queryBuilders">queryBuilders.</param>
        /// <param name="queryExecutors">queryExecutors.</param>
        /// <param name="dbExceptionsParsers">dbExceptionParsers.</param>
        public QueryManagerFactory(IEnumerable<IQueryBuilder> queryBuilders,
            IEnumerable<IQueryExecutor> queryExecutors,
            IEnumerable<DbExceptionParser> dbExceptionsParsers)
        {
            _queryBuilders = queryBuilders.ToDictionary(provider => provider.DeriveDatabaseType(), provider => provider);
            _queryExecutors = queryExecutors.ToDictionary(provider => provider.DeriveDatabaseType(), provider => provider);
            _dbExceptionsParsers = dbExceptionsParsers.ToDictionary(provider => provider.DeriveDatabaseType(), provider => provider);
        }

        /// <inheritdoc />
        public IQueryBuilder GetQueryBuilder(DatabaseType databaseType)
        {
            if (!_queryBuilders.ContainsKey(databaseType))
            {
                throw new DataApiBuilderException($"{nameof(DatabaseType)}:{databaseType} could not be found within the config", HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
            }

            return _queryBuilders[databaseType];
        }

        /// <inheritdoc />
        public IQueryExecutor GetQueryExecutor(DatabaseType databaseType)
        {
            if (!_queryExecutors.ContainsKey(databaseType))
            {
                throw new DataApiBuilderException($"{nameof(databaseType)}:{databaseType} could not be found within the config", HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
            }

            return _queryExecutors[databaseType];
        }

        /// <inheritdoc />
        public DbExceptionParser GetDbExceptionParser(DatabaseType databaseType)
        {
            if (!_dbExceptionsParsers.ContainsKey(databaseType))
            {
                throw new DataApiBuilderException($"{nameof(databaseType)}:{databaseType} could not be found within the config", HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
            }

            return _dbExceptionsParsers[databaseType];
        }

    }
}
