using System.Collections.Generic;

namespace Azure.DataGateway.Service.Resolvers
{
    ///<summary>
    /// QueryStructure provides an intermediate represtation of a SQL query. This
    ///intermediate structure can be used to generate a Postgres or MSSQL query.
    /// In some sense this is an AST (abstract syntax tree) of a SQL query.
    /// However, it only supports the very limited set of SQL constructs that we
    /// are needed to represent a GraphQL query as SQL.
    ///</summary>
    public class QueryStructure
    {
        /// <summary>
        /// The target Entity to be queried.
        /// </summary>
        public string EntityName { get; set; }

        /// <summary>
        /// Field names of the entity and their aliases.
        /// </summary>
        public List<string> Fields { get; set; }

        /// <summary>
        /// Conditions to be added to the query.
        /// </summary>
        public List<string> Conditions { get; set; }

        /// <summary>
        /// Parameter values.
        /// </summary>
        public IDictionary<string, object> Parameters { get; set; }

        /// <summary>
        /// The target Entity to be queried.
        /// </summary>
        public QueryStructure(string entityName)
        {
            EntityName = entityName;
        }
    }
}
