using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// This interface defines methods for database validation.
    /// </summary>
    public interface IEntitySchema
    {
        /// <summary>
        /// This method should return true if the specified field exists in the database.
        /// If the column does not exist it should return false.
        /// </summary>
        /// <param name="database"> Database name.</param>
        /// <param name="schema"> Schema name.</param>
        /// <param name="table"> Table name.</param>
        /// <param name="column"> The column we want to check.</param>
        /// <returns>If the column exists.</returns>
        public bool IsColumnExists(string database, string schema, string table, string column);

        /// <summary>
        /// Get the primary key for the specific entity with specific schema in specific database
        /// </summary>
        /// <param name="database"> Database name.</param>
        /// <param name="schema"> Schema name.</param>
        /// <param name="table"> Table name.</param>
        /// <returns> The list of primary key column.</returns>
        public Task<> GetPrimaryKeyAsync(string database, string schema, string table);
    }
}
