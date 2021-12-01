using System.Collections.Generic;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// RestCondition is a class that represents a parsed condition that can be
    /// specified in the rest calls.
    /// </summary>
    public class RestCondition
    {
        public string Field { get; set; }
        public string Value { get; set; }
        public RestCondition(string field, string value)
        {
            Field = field;
            Value = value;
        }
    }

    ///<summary>
    /// FindRequestContext provides the major components of a REST or GraphQL query
    /// corresponding to the FindById or FindMany operations.
    ///</summary>
    public class FindRequestContext
    {
        /// <summary>
        /// The target Entity to be queried.
        /// </summary>
        public string EntityName { get; set; }

        /// <summary>
        /// Field names of the entity that are requested.
        /// </summary>
        public List<string> Fields { get; set; }

        /// <summary>
        /// Conditions to be that are defined by the request.
        /// </summary>
        public List<RestCondition> Conditions { get; set; }

        /// <summary>
        /// Is the result supposed to be a list or not.
        /// </summary>
        public bool IsListQuery { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public FindRequestContext(string entityName, bool isList)
        {
            EntityName = entityName;
            Fields = new();
            IsListQuery = isList;
            Conditions = new();
        }
    }
}
