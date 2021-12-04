
namespace Azure.DataGateway.Service.Resolvers
{
    // <summary>
    // Interface for building of the final query string. This is only necessary
    // when you use the SqlQueryEngine, thus for SQL based databases (e.g. not
    // Cosmos).
    // </summary>
    public interface IQueryBuilder
    {
        // <summary>
        // Modifies the inputQuery in such a way that it returns the results as
        // a JSON string.
        // </summary>
        public string Build(string inputQuery, bool isList);

        /// <summary>
        /// Builds the query specific to the target database for the given FindQueryStructure
        /// object which holds the major components of the query.
        /// </summary>
        public string Build(FindQueryStructure structure);
    }
}
