using System;
using System.Collections.Generic;
using System.Data.Common;
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
        /// <param name="columnNames">List of columns to extract. Extracts all if unspecified.</param>
        ///<returns>An object formed using the results of the query as returned by the given handler.</returns>
        public Task<TResult?> ExecuteQueryAsync<TResult>(
            string sqltext,
            IDictionary<string, object?> parameters,
            Func<DbDataReader, List<string>?, Task<TResult?>> dataReaderHandler,
            List<string>? columnNames = null);

        /// <summary>
        /// Extract the rows from the given db data reader to populate
        /// the JsonArray to be returned.
        /// </summary>
        /// <param name="dbDataReader">A Db data reader.</param>
        /// <param name="columnNames">List of columns to extract. Extracts all if unspecified.</param>
        /// <returns>A JsonArray with each element corresponding to the row in the dbDataReader.</returns>
        public Task<JsonArray?> GetJsonArrayAsync(
            DbDataReader dbDataReader,
            List<string>? columnNames = null);

        /// <summary>
        /// Extract the rows from the given db data reader to deserialize into
        /// a Json object of type TResult to be returned.
        /// </summary>
        /// <param name="dbDataReader">A Db data reader.</param>
        /// <returns>A Json object of type TResult.</returns>
        public Task<TResult?> GetJsonResultAsync<TResult>(
            DbDataReader dbDataReader,
            List<string>? columnNames = null);

        /// <summary>
        /// Extracts a single row from DbDataReader and format it so it can be used as a parameter to a query execution
        /// </summary>
        /// <param name="dbDataReader">A Db data reader</param>
        /// <param name="columnNames">List of columns to extract. Extracts all if unspecified.</param>
        ///<returns>A tuple of 2 dictionaries:
        /// 1. A dictionary representing the row in <c>ColumnName: Value</c> format, null if no row was found
        /// 2. A dictionary of properties of the Db Data Reader to indicate the characteristics of the result.</returns>
        public Task<Tuple<Dictionary<string, object?>?, Dictionary<string, object>>?>
            ExtractRowFromDbDataReader(
                DbDataReader dbDataReader,
                List<string>? columnNames = null);

        public Task<Tuple<Dictionary<string, object?>?, Dictionary<string, object>>?>
            GetMultipleResultIfAnyAsync(
                DbDataReader dbDataReader,
                List<string>? columnNames = null);

        public Task<Dictionary<string, object>?>
            GetResultProperties(
                DbDataReader dbDataReader,
                List<string>? columnNames = null);

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
    }
}
