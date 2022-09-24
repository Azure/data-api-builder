
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Resolvers
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
        /// Builds the query to obtain foreign key information with the given
        /// number of parameters.
        /// </summary>
        public string BuildForeignKeyInfoQuery(int numberOfParameters, bool developerMode, ILogger logger);

        /// <summary>
        /// Adds database specific quotes to string identifier
        /// </summary>
        public string QuoteIdentifier(string identifier);
    }
}
