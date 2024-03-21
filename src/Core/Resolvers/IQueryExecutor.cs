// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Interface for query execution for Sql like databases (eg. MsSql, PostgreSql).
    /// </summary>
    public interface IQueryExecutor
    {
        /// <summary>
        /// Executes sql text with the given parameters and
        /// uses the function dataReaderHandler to process
        /// the results from the DbDataReader and return into an object of type TResult.
        /// </summary>
        /// <param name="sqltext">SQL text to be executed.</param>
        /// <param name="parameters">The parameters used to execute the SQL text.</param>
        /// <param name="dataReaderHandler">The function to invoke to handle the results
        /// in the DbDataReader obtained after executing the query.</param>
        /// <param name="httpContext">Current request httpContext.</param>
        /// <param name="args">List of string arguments to the DbDataReader handler.</param>
        /// <param name="dataSourceName">dataSourceName against which to run query. Can specify null or empty to run against default db.</param>
        /// <returns>An object formed using the results of the query as returned by the given handler.</returns>
        public Task<TResult?> ExecuteQueryAsync<TResult>(
            string sqltext,
            IDictionary<string, DbConnectionParam> parameters,
            Func<DbDataReader, List<string>?, Task<TResult>>? dataReaderHandler,
            string dataSourceName,
            HttpContext? httpContext = null,
            List<string>? args = null);

        /// <summary>
        /// Extracts the rows from the given DbDataReader to populate
        /// the JsonArray to be returned.
        /// </summary>
        /// <param name="dbDataReader">A DbDataReader.</param>
        /// <param name="args">List of string arguments if any.</param>
        /// <returns>A JsonArray with each element corresponding to the row (ColumnName : columnValue) in the dbDataReader.</returns>
        public Task<JsonArray> GetJsonArrayAsync(
            DbDataReader dbDataReader,
            List<string>? args = null);

        /// <summary>
        /// Extracts the rows from the given DbDataReader to deserialize into
        /// a Json object of type TResult to be returned.
        /// </summary>
        /// <param name="dbDataReader">A DbDataReader.</param>
        /// <param name="args">List of string arguments if any.</param>
        /// <returns>A Json object of type TResult.</returns>
        public Task<TResult?> GetJsonResultAsync<TResult>(
            DbDataReader dbDataReader,
            List<string>? args = null);

        /// <summary>
        /// Extracts the current Result Set of DbDataReader and format it
        /// so it can be used as a parameter to query execution.
        /// </summary>
        /// <param name="dbDataReader">A DbDataReader</param>
        /// <param name="args">List of columns to extract. Extracts all if unspecified.</param>
        /// <returns>Current Result Set in the DbDataReader.</returns>
        public Task<DbResultSet> ExtractResultSetFromDbDataReader(
                DbDataReader dbDataReader,
                List<string>? args = null);

        /// <summary>
        /// Extracts the result set corresponding to the operation (update/insert) being executed.
        /// For PgSql,MySql, returns the first result set (among the two for update/insert) having non-zero affected rows.
        /// For MsSql, returns the only result set having non-zero affected rows which corresponds to either update/insert operation.
        /// </summary>
        /// <param name="dbDataReader">A DbDataReader.</param>
        /// <param name="args">The arguments to this handler - args[0] = primary key in pretty format, args[1] = entity name.</param>
        /// <returns>Single row read from DbDataReader.
        /// If the first result set is being returned, DbResultSet.ResultProperties dictionary has
        /// the property "IsUpdateResultSet" set to true.</returns>
        public Task<DbResultSet> GetMultipleResultSetsIfAnyAsync(
                DbDataReader dbDataReader,
                List<string>? args = null);

        /// <summary>
        /// Gets the result properties like RecordsAffected, HasRows in a dictionary.
        /// </summary>
        /// <param name="dbDataReader">A DbDataReader.</param>
        /// <param name="args">List of string arguments if any.</param>
        /// <returns>A dictionary of properties of the DbDataReader like RecordsAffected, HasRows.</returns>
        public Task<Dictionary<string, object>> GetResultProperties(
                DbDataReader dbDataReader,
                List<string>? args = null);

        /// <summary>
        /// Wrapper for DbDataReader.ReadAsync.
        /// This will catch certain db errors and throw an exception which can
        /// be reported to the user
        /// </summary>
        public Task<bool> ReadAsync(DbDataReader reader);

        /// <summary>
        /// Modified the properties of the supplied connection to support managed identity access.
        /// </summary>
        public Task SetManagedIdentityAccessTokenIfAnyAsync(DbConnection conn, string dataSourceName);

        /// <summary>
        /// Method to generate the query to send user data to the underlying database which might be used
        /// for additional security at the database level.
        /// </summary>
        /// <param name="httpContext">Current user httpContext.</param>
        /// <param name="parameters">Dictionary of parameters/value required to execute the query.</param>
        /// <param name="dataSourceName"> Db for which to generate query.</param>
        /// <returns>empty string / query to set session parameters for the connection.</returns>
        public string GetSessionParamsQuery(HttpContext? httpContext, IDictionary<string, DbConnectionParam> parameters, string dataSourceName);

        /// <summary>
        /// Helper method to populate DbType for parameter. Currently DbTypes for parameters are only populated for MsSql.
        /// </summary>
        /// <param name="parameterEntry">Entry corresponding to current database parameter to be created.</param>
        /// <param name="parameter">Parameter sent to database.</param>
        public void PopulateDbTypeForParameter(KeyValuePair<string, DbConnectionParam> parameterEntry, DbParameter parameter);
    }
}
