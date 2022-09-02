using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Interface for query execution for Sql like databases (eg. MsSql, PostgreSql).
    /// </summary>
    public interface IQueryExecutor
    {
        /// <summary>
        /// Execute sql text with parameters and return result set.
        /// </summary>
        /// <param name="sqltext">SQL text to be executed.</param>
        /// <param name="parameters">The parameters used to execute the SQL text.</param>
        /// <returns>DbDataReader object for reading the result set.</returns>
        public Task<DbDataReader> ExecuteQueryAsync(string sqltext, IDictionary<string, object?> parameters);

        /// <summary>
        /// Wrapper for DbDataReader.ReadAsync.
        /// This will catch certain db errors and throw an exception which can
        /// be reported to the user
        /// </summary>
        public Task<bool> ReadAsync(DbDataReader reader);

        /// <summary>
        /// Extracts a single row from DbDataReader and format it so it can be used as a parameter to a query execution
        /// </summary>
        /// <param name="onlyExtract">List of columns to extract. Extracts all if unspecified.</param>
        ///<returns>A dictionary representating the row in <c>ColumnName: Value</c> format, null if no row was found</returns>
        public Task<Dictionary<string, object?>?> ExtractRowFromDbDataReader(DbDataReader dbDataReader, List<string>? onlyExtract = null);

        /// <summary>
        /// Modified the properties of the supplied connection to support managed identity access.
        /// </summary>
        public Task HandleManagedIdentityAccessIfAny(DbConnection conn);
    }
}
