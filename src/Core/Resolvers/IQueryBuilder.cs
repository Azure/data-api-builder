// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Resolvers
{
    // <summary>
    // Interface for building of the final query string. This is only necessary
    // when you use the SqlQueryEngine, thus for SQL based databases (e.g. not
    // Cosmos).
    // </summary>
    public interface IQueryBuilder
    {
        /// <summary>
        /// Builds the query specific to the target database for the given
        /// SqlQueryStructure object which holds the major components of the
        /// query.
        /// </summary>
        public string Build(SqlQueryStructure structure);

        /// <summary>
        /// Builds the query specific to the target database for the given
        /// SqlInsertStructure object which holds the major components of the
        /// query.
        /// </summary>
        public string Build(SqlInsertStructure structure);
        /// <summary>
        /// Builds the query specific to the target database for the given
        /// SqlInsertStructure object which holds the major components of the
        /// query.
        /// </summary>
        public string Build(SqlUpdateStructure structure);
        /// Builds the query specific to the target database for the given
        /// SqlDeleteStructure object which holds the major components of the
        /// query.
        /// </summary>
        public string Build(SqlDeleteStructure structure);
        /// Builds the query specific to the target database for the given
        /// SqlUpsertQueryStructure object which holds the major components of the
        /// query.
        /// </summary>
        public string Build(SqlUpsertQueryStructure structure);

        /// <summary>
        /// Builds the query specific to the target database for the given
        /// SqlExecuteStructure object which holds the major components of the
        /// query.
        /// </summary>
        public string Build(SqlExecuteStructure structure);

        /// <summary>
        /// Builds a query that selects 1 and only has predicates.
        /// </summary>
        public string Build(BaseSqlQueryStructure structure);

        /// <summary>
        /// Builds the query to obtain foreign key information with the given
        /// number of parameters.
        /// </summary>
        public string BuildForeignKeyInfoQuery(int numberOfParameters);

        /// <summary>
        /// Builds the query to obtain details about the result set for stored-procedure
        /// </summary>
        /// <param name="databaseObjectName">Name of stored-procedure</param>
        /// <returns></returns>
        public string BuildStoredProcedureResultDetailsQuery(string databaseObjectName);

        /// <summary>
        /// Returns the query to get the read-only columns present in the table.
        /// </summary>
        /// <param name="schemaOrDatabaseParamName">Param name of the schema/database.</param>
        /// <param name="tableParamName">Param name of the table.</param>
        /// <exception cref="NotImplementedException">Thrown when class implementing the interface has not provided
        /// an overridden implementation of the method.</exception>
        string BuildQueryToGetReadOnlyColumns(string schemaOrDatabaseParamName, string tableParamName);

        /// <summary>
        /// Builds the query to determine the number of enabled triggers on a database table.
        /// Needed only for MsSql.
        /// </summary>
        string BuildFetchEnabledTriggersQuery() => throw new NotImplementedException();

        /// <summary>
        /// Adds database specific quotes to string identifier
        /// </summary>
        public string QuoteIdentifier(string identifier);
    }
}
