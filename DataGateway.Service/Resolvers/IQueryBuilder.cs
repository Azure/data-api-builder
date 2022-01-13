
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
        /// Enclose the given string in the delimiters that are used to quote
        /// identifiers, e.g. double quotes.
        /// </summary>
        /// <param name="ident">The unquoted identifier to be enclosed.</param>
        /// <returns>The quoted identifier.</returns>
        public string QuoteIdentifier(string ident);

        /// <summary>
        /// Wrap a column that corresponds to a subquery in whatever SQL that
        /// is necassary to select it as a JSON Field.
        /// </summary>
        /// <param name="column">The column that corresponds to a subquery</param>
        /// <param name="subquery">The subquery that the column corresponds to</param>
        /// <returns>The wrapped column.</returns>
        public string WrapSubqueryColumn(string column, SqlQueryStructure subquery);
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
        /// <summary>
        /// Simply a quoted version of the identifier "data". This identifier
        /// is commonly used throughout the query.
        /// </summary>
        public string DataIdent { get; }
    }
}
