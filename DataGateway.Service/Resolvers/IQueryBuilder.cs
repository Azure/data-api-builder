namespace Azure.DataGateway.Service.Resolvers
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
        /// Builds the query to obtain foreign key information with the given
        /// number of parameters.
        /// </summary>
        public string BuildForeignKeyInfoQuery(int numberOfParameters);

        /// <summary>
        /// Creates a list of named parameters with incremental suffixes
        /// starting from 0 to numberOfParameters - 1.
        /// e.g. tableName0, tableName1
        /// </summary>
        /// <param name="kindOfParam">The kind of parameter being created acting
        /// as the prefix common to all parameters.</param>
        /// <param name="numberOfParameters">The number of parameters to create.</param>
        /// <returns>The created list</returns>
        public string[] CreateParams(string kindOfParam, int numberOfParameters);
    }
}
