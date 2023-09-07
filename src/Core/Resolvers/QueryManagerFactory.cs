// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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
        /// <param name="runtimeConfigProvider">runtimeconfigprovider.</param>
        /// <param name="logger">logger.</param>
        /// <param name="contextAccessor">httpcontextaccessor.</param>
        public QueryManagerFactory(RuntimeConfigProvider runtimeConfigProvider, ILogger<IQueryExecutor> logger, IHttpContextAccessor contextAccessor)
        {
            _queryBuilders = new Dictionary<DatabaseType, IQueryBuilder>();
            _queryExecutors = new Dictionary<DatabaseType, IQueryExecutor>();
            _dbExceptionsParsers = new Dictionary<DatabaseType, DbExceptionParser>();
            foreach ((string dataSourceName, DataSource dataSource) in runtimeConfigProvider.GetConfig().GetDataSourceNamesToDataSourcesIterator())
            {
                IQueryBuilder? queryBuilder = null;
                IQueryExecutor? queryExecutor = null;
                DbExceptionParser? exceptionParser = null;

                switch (dataSource.DatabaseType)
                {
                    case DatabaseType.CosmosDB_NoSQL:
                        break;
                    case DatabaseType.MSSQL:
                        queryBuilder = new MsSqlQueryBuilder();
                        exceptionParser = new MsSqlDbExceptionParser(runtimeConfigProvider);
                        queryExecutor = new MsSqlQueryExecutor(runtimeConfigProvider, exceptionParser, logger, contextAccessor);
                        break;
                    case DatabaseType.MySQL:
                        queryBuilder = new MySqlQueryBuilder();
                        exceptionParser = new MySqlDbExceptionParser(runtimeConfigProvider);
                        queryExecutor = new MySqlQueryExecutor(runtimeConfigProvider, exceptionParser, logger, contextAccessor);
                        break;
                    case DatabaseType.PostgreSQL:
                        queryBuilder = new PostgresQueryBuilder();
                        exceptionParser = new PostgreSqlDbExceptionParser(runtimeConfigProvider);
                        queryExecutor = new PostgreSqlQueryExecutor(runtimeConfigProvider, exceptionParser, logger, contextAccessor);
                        break;
                    default:
                        throw new NotSupportedException(dataSource.DatabaseTypeNotSupportedMessage);
                }

                _queryBuilders.TryAdd(dataSource.DatabaseType, queryBuilder!);
                _queryExecutors.TryAdd(dataSource.DatabaseType, queryExecutor!);
                _dbExceptionsParsers.TryAdd(dataSource.DatabaseType, exceptionParser!);
            }
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
