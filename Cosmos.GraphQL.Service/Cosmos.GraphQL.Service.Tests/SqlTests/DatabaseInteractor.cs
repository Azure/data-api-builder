using Cosmos.GraphQL.Service.Resolvers;

namespace Cosmos.GraphQL.Service.Tests.Sql
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
        /// Creates a table in the database
        /// </summary>
        public void CreateTable(string tableName, string columns)
        {
            _ = QueryExecutor.ExecuteQueryAsync($"CREATE TABLE {tableName} ({columns});", null).Result;
        }

        /// <summary>
        /// Creates a database
        /// </summary>
        public void CreateDatabase(string databaseName)
        {
            _ = QueryExecutor.ExecuteQueryAsync($"CREATE DATABASE {databaseName};", null).Result;
        }

        /// <summary>
        /// Drops all tables in the database
        /// </summary>
        public void DropTables()
        {
            // Drops all tables in the database.
            string dropAllTables = @"

                DECLARE @sql NVARCHAR(max)=''

                SELECT @sql += ' Drop table ' + QUOTENAME(TABLE_SCHEMA) + '.'+ QUOTENAME(TABLE_NAME) + '; '
                FROM   INFORMATION_SCHEMA.TABLES
                WHERE  TABLE_TYPE = 'BASE TABLE'

                Exec Sp_executesql @sql";

            _ = QueryExecutor.ExecuteQueryAsync(dropAllTables, null).Result;
        }
    }
}
