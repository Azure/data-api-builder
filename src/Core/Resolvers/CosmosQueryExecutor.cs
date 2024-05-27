// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    internal class CosmosQueryExecutor : IQueryExecutor
    {
        protected ILogger<IQueryExecutor> QueryExecutorLogger { get; }
        protected IHttpContextAccessor HttpContextAccessor { get; }

        /// <summary>
        /// The managed identity Access Token string obtained
        /// from the configuration controller.
        /// Key: datasource name, Value: access token for this datasource.
        /// </summary>
        private readonly Dictionary<string, string?> _accessTokensFromConfiguration;

        public CosmosQueryExecutor(
          RuntimeConfigProvider runtimeConfigProvider,
          ILogger<IQueryExecutor> logger,
          IHttpContextAccessor httpContextAccessor)
        {
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();
            IEnumerable<KeyValuePair<string, DataSource>> cosmosDbs = runtimeConfig.GetDataSourceNamesToDataSourcesIterator().Where(x => x.Value.DatabaseType is DatabaseType.MSSQL || x.Value.DatabaseType is DatabaseType.DWSQL);

            _accessTokensFromConfiguration = runtimeConfigProvider.ManagedIdentityAccessToken;

            QueryExecutorLogger = logger;
            HttpContextAccessor = httpContextAccessor;
            foreach ((string dataSourceName, DataSource dataSource) in cosmosDbs)
            {
                
            }
        }

        public TResult? ExecuteQuery<TResult>(string sqltext, IDictionary<string, DbConnectionParam> parameters, Func<DbDataReader, List<string>?, TResult>? dataReaderHandler, HttpContext? httpContext = null, List<string>? args = null, string dataSourceName = "")
        {
            throw new NotImplementedException();
        }

        public Task<TResult?> ExecuteQueryAsync<TResult>(string sqltext, IDictionary<string, DbConnectionParam> parameters, Func<DbDataReader, List<string>?, Task<TResult>>? dataReaderHandler, string dataSourceName, HttpContext? httpContext = null, List<string>? args = null)
        {
            throw new NotImplementedException();
        }

        public DbResultSet ExtractResultSetFromDbDataReader(DbDataReader dbDataReader, List<string>? args = null)
        {
            throw new NotImplementedException();
        }

        public Task<DbResultSet> ExtractResultSetFromDbDataReaderAsync(DbDataReader dbDataReader, List<string>? args = null)
        {
            throw new NotImplementedException();
        }

        public Task<JsonArray> GetJsonArrayAsync(DbDataReader dbDataReader, List<string>? args = null)
        {
            throw new NotImplementedException();
        }

        public Task<TResult?> GetJsonResultAsync<TResult>(DbDataReader dbDataReader, List<string>? args = null)
        {
            throw new NotImplementedException();
        }

        public Task<DbResultSet> GetMultipleResultSetsIfAnyAsync(DbDataReader dbDataReader, List<string>? args = null)
        {
            throw new NotImplementedException();
        }

        public Dictionary<string, object> GetResultProperties(DbDataReader dbDataReader, List<string>? args = null)
        {
            throw new NotImplementedException();
        }

        public Task<Dictionary<string, object>> GetResultPropertiesAsync(DbDataReader dbDataReader, List<string>? args = null)
        {
            throw new NotImplementedException();
        }

        public string GetSessionParamsQuery(HttpContext? httpContext, IDictionary<string, DbConnectionParam> parameters, string dataSourceName)
        {
            throw new NotImplementedException();
        }

        public void PopulateDbTypeForParameter(KeyValuePair<string, DbConnectionParam> parameterEntry, DbParameter parameter)
        {
            throw new NotImplementedException();
        }

        public bool Read(DbDataReader reader)
        {
            throw new NotImplementedException();
        }

        public Task<bool> ReadAsync(DbDataReader reader)
        {
            throw new NotImplementedException();
        }

        public Task SetManagedIdentityAccessTokenIfAnyAsync(DbConnection conn, string dataSourceName)
        {
            throw new NotImplementedException();
        }
    }
}
