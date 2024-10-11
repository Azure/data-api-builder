// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using static Azure.DataApiBuilder.Config.DabConfigEvents;

namespace Azure.DataApiBuilder.Core.Resolvers.Factories
{
    /// <summary>
    /// QueryManagerFactory. Implements IQueryManagerFactory
    /// Used to get the appropriate query builder, query executor and exception parser and  based on the database type.
    /// </summary>
    public class QueryManagerFactory : IAbstractQueryManagerFactory
    {
        // Internally mutated during Hot-Reload
        private IDictionary<DatabaseType, IQueryBuilder> _queryBuilders;
        private IDictionary<DatabaseType, IQueryExecutor> _queryExecutors;
        private IDictionary<DatabaseType, DbExceptionParser> _dbExceptionsParsers;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly ILogger<IQueryExecutor> _logger;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly HotReloadEventHandler<HotReloadEventArgs>? _handler;

        /// <summary>
        /// Initiates an instance of QueryManagerFactory
        /// </summary>
        /// <param name="runtimeConfigProvider">runtimeconfigprovider.</param>
        /// <param name="logger">logger.</param>
        /// <param name="contextAccessor">httpcontextaccessor.</param>
        public QueryManagerFactory(
            RuntimeConfigProvider runtimeConfigProvider,
            ILogger<IQueryExecutor> logger,
            IHttpContextAccessor contextAccessor,
            HotReloadEventHandler<HotReloadEventArgs>? handler)
        {
            handler?.Subscribe(QUERY_MANAGER_FACTORY_ON_CONFIG_CHANGED, OnConfigChanged);
            _handler = handler;
            _runtimeConfigProvider = runtimeConfigProvider;
            _logger = logger;
            _contextAccessor = contextAccessor;
            _queryBuilders = new Dictionary<DatabaseType, IQueryBuilder>();
            _queryExecutors = new Dictionary<DatabaseType, IQueryExecutor>();
            _dbExceptionsParsers = new Dictionary<DatabaseType, DbExceptionParser>();

            ConfigureQueryManagerFactory();
        }

        private void ConfigureQueryManagerFactory()
        {

            foreach (DataSource dataSource in _runtimeConfigProvider.GetConfig().ListAllDataSources())
            {
                IQueryBuilder? queryBuilder = null;
                IQueryExecutor? queryExecutor = null;
                DbExceptionParser? exceptionParser = null;

                if (_queryBuilders.ContainsKey(dataSource.DatabaseType))
                {
                    // we have already created the builder, parser and executor for this database type.No need to create again.
                    continue;
                }

                switch (dataSource.DatabaseType)
                {
                    case DatabaseType.CosmosDB_NoSQL:
                        break;
                    case DatabaseType.MSSQL:
                        queryBuilder = new MsSqlQueryBuilder();
                        exceptionParser = new MsSqlDbExceptionParser(_runtimeConfigProvider);
                        queryExecutor = new MsSqlQueryExecutor(_runtimeConfigProvider, exceptionParser, _logger, _contextAccessor, _handler);
                        break;
                    case DatabaseType.MySQL:
                        queryBuilder = new MySqlQueryBuilder();
                        exceptionParser = new MySqlDbExceptionParser(_runtimeConfigProvider);
                        queryExecutor = new MySqlQueryExecutor(_runtimeConfigProvider, exceptionParser, _logger, _contextAccessor, _handler);
                        break;
                    case DatabaseType.PostgreSQL:
                        queryBuilder = new PostgresQueryBuilder();
                        exceptionParser = new PostgreSqlDbExceptionParser(_runtimeConfigProvider);
                        queryExecutor = new PostgreSqlQueryExecutor(_runtimeConfigProvider, exceptionParser, _logger, _contextAccessor, _handler);
                        break;
                    case DatabaseType.DWSQL:
                        queryBuilder = new DwSqlQueryBuilder();
                        exceptionParser = new MsSqlDbExceptionParser(_runtimeConfigProvider);
                        queryExecutor = new MsSqlQueryExecutor(_runtimeConfigProvider, exceptionParser, _logger, _contextAccessor, _handler);
                        break;
                    default:
                        throw new NotSupportedException(dataSource.DatabaseTypeNotSupportedMessage);
                }

                _queryBuilders.TryAdd(dataSource.DatabaseType, queryBuilder!);
                _queryExecutors.TryAdd(dataSource.DatabaseType, queryExecutor!);
                _dbExceptionsParsers.TryAdd(dataSource.DatabaseType, exceptionParser!);
            }
        }

        public void OnConfigChanged(object? sender, HotReloadEventArgs args)
        {
            _queryBuilders = new Dictionary<DatabaseType, IQueryBuilder>();
            _queryExecutors = new Dictionary<DatabaseType, IQueryExecutor>();
            _dbExceptionsParsers = new Dictionary<DatabaseType, DbExceptionParser>();
            ConfigureQueryManagerFactory();
        }

        /// <inheritdoc />
        public IQueryBuilder GetQueryBuilder(DatabaseType databaseType)
        {
            if (!_queryBuilders.TryGetValue(databaseType, out IQueryBuilder? queryBuilder))
            {
                throw new DataApiBuilderException(
                    $"{nameof(DatabaseType)}:{databaseType} could not be found within the config",
                    HttpStatusCode.BadRequest,
                    DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
            }

            return queryBuilder;
        }

        /// <inheritdoc />
        public IQueryExecutor GetQueryExecutor(DatabaseType databaseType)
        {
            if (!_queryExecutors.TryGetValue(databaseType, out IQueryExecutor? queryExecutor))
            {
                throw new DataApiBuilderException(
                    $"{nameof(databaseType)}:{databaseType} could not be found within the config",
                    HttpStatusCode.BadRequest,
                    DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
            }

            return queryExecutor;
        }

        /// <inheritdoc />
        public DbExceptionParser GetDbExceptionParser(DatabaseType databaseType)
        {
            if (!_dbExceptionsParsers.TryGetValue(databaseType, out DbExceptionParser? exceptionParser))
            {
                throw new DataApiBuilderException(
                    $"{nameof(databaseType)}:{databaseType} could not be found within the config",
                    HttpStatusCode.BadRequest,
                    DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
            }

            return exceptionParser;
        }

    }
}
