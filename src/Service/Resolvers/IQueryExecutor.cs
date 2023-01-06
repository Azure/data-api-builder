using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Azure.DataApiBuilder.Service.Resolvers
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
        /// <param name="args">List of string arguments to the DbDataReader handler.</param>
        ///<returns>An object formed using the results of the query as returned by the given handler.</returns>
        public Task<TResult?> ExecuteQueryAsync<TResult>(
            string sqltext,
            IDictionary<string, object?> parameters,
            Func<DbDataReader, List<string>?, Task<TResult?>>? dataReaderHandler,
            Dictionary<string, Claim>? claimsDictionary = null,
            List<string>? args = null);

        /// <summary>
        /// Extracts the rows from the given DbDataReader to populate
        /// the JsonArray to be returned.
        /// </summary>
        /// <param name="dbDataReader">A DbDataReader.</param>
        /// <param name="args">List of string arguments if any.</param>
        /// <returns>A JsonArray with each element corresponding to the row (ColumnName : columnValue) in the dbDataReader.</returns>
        public Task<JsonArray?> GetJsonArrayAsync(
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
        /// Extracts a single row from DbDataReader and format it
        /// so it can be used as a parameter to query execution.
        /// </summary>
        /// <param name="dbDataReader">A DbDataReader</param>
        /// <param name="args">List of columns to extract. Extracts all if unspecified.</param>
        /// <returns>A tuple of 2 dictionaries:
        /// 1. A dictionary representing the row in <c>ColumnName: Value</c> format, null if no row was found
        /// 2. A dictionary of properties of the Db Data Reader like RecordsAffected, HasRows.</returns>
        public Task<Tuple<Dictionary<string, object?>?, Dictionary<string, object>>?> ExtractRowFromDbDataReader(
                DbDataReader dbDataReader,
                List<string>? args = null);

        /// <summary>
        /// Extracts first result set and returns it immediately if it has > 0 rows.
        /// If no rows, tries to get the subsequent result set if any.
        /// Throws an exception if the second result is null as well.
        /// </summary>
        /// <param name="dbDataReader">A DbDataReader.</param>
        /// <param name="args">The arguments to this handler - args[0] = primary key in pretty format, args[1] = entity name.</param>
        /// <returns>A tuple of 2 dictionaries:
        /// 1. A dictionary representing the row in <c>ColumnName: Value</c> format.
        /// 2. A dictionary of properties of the DbDataReader like RecordsAffected, HasRows.
        /// If the first result set is being returned, has the property "IsFirstResultSet" set to true in this dictionary.</returns>
        public Task<Tuple<Dictionary<string, object?>?, Dictionary<string, object>>?> GetMultipleResultSetsIfAnyAsync(
                DbDataReader dbDataReader,
                List<string>? args = null);

        /// <summary>
        /// Gets the result properties like RecordsAffected, HasRows in a dictionary.
        /// </summary>
        /// <param name="dbDataReader">A DbDataReader.</param>
        /// <param name="args">List of string arguments if any.</param>
        /// <returns>A dictionary of properties of the DbDataReader like RecordsAffected, HasRows.</returns>
        public Task<Dictionary<string, object>?> GetResultProperties(
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
        public Task SetManagedIdentityAccessTokenIfAnyAsync(DbConnection conn);

        /// <summary>
        /// Method to generate the query to send user data to the underlying database which might be used
        /// for additional security at the database level.
        /// </summary>
        /// <param name="claimsDictionary">Dictionary containing all the claims belonging to the user.</param>
        /// <returns></returns>
        public string GetSessionMapQuery(Dictionary<string, Claim> claimsDictionary);
    }
}
