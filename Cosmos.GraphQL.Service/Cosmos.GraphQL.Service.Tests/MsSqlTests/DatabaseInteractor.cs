using Cosmos.GraphQL.Service.Resolvers;

namespace Cosmos.GraphQL.Service.Tests.MsSql
{
    /// <summary>
    /// Class that provides functions to interact with a database.
    /// </summary>
    public class DatabaseInteractor
    {
        public IQueryExecutor QueryExecutor { get; private set; }

        public DatabaseInteractor(IQueryExecutor queryExecutor)
        {
            QueryExecutor = queryExecutor;
        }

        /// <summary>
        /// Inserts data into the database.
        /// </summary>
        public void InsertData(string tableName, string values)
        {
            _ = QueryExecutor.ExecuteQueryAsync($"INSERT INTO {tableName} VALUES({values});", null).Result;
        }

        /// <summary>
        /// Creates a table in the database with provided name and columns
        /// </summary>
        public void CreateTable(string tableName, string columns)
        {
            _ = QueryExecutor.ExecuteQueryAsync($"CREATE TABLE {tableName} ({columns});", null).Result;
        }

        /// <summary>
        /// Drops all tables in the database
        /// </summary>
        public void DropTable(string tableName)
        {
            // Drops all tables in the database.
            string dropTable = string.Format(
                @"DROP TABLE IF EXISTS {0};", tableName);

            _ = QueryExecutor.ExecuteQueryAsync(sqltext: dropTable, parameters: null).Result;
        }
    }
}
