using System.Collections.Generic;

namespace Azure.DataGateway.Service.Resolvers
{
    ///<summary>
    /// FindQueryStructure provides the major components of a REST or GraphQL query
    /// corresponding to the FindById or FindMany operations.
    ///</summary>
    public class FindQueryStructure
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
        /// Is the result supposed to be a list or not.
        /// </summary>
        public bool IsListQuery { get; set; }

        /// <summary>
        /// Parameter values.
        /// </summary>
        public IDictionary<string, object> Parameters { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public FindQueryStructure(string entityName, bool isList)
        {
            EntityName = entityName;
            IsListQuery = isList;
        }
    }
}
