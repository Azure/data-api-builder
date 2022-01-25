using System.Collections.Generic;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// RestPredicate is a class that represents a parsed predicate that can be
    /// specified in the rest calls.
    /// </summary>
    public class RestPredicate
    {
        /// <summary>
        /// The field that is compared in the predicate.
        /// </summary>
        public string Field { get; set; }
        /// <summary>
        /// The value to which the field is compared.
        /// </summary>
        public string Value { get; set; }

        public bool IsLookUp { get; set; }

        public RestPredicate(string field, string value, bool isLookUp)
        {
            Field = field;
            Value = value;
            IsLookUp = isLookUp;
        }
    }

    /// <summary>
    /// FindRequestContext provides the major components of a REST or GraphQL query
    /// corresponding to the FindById or FindMany operations.
    /// </summary>
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
        /// Predicates to be that are defined by the request.
        /// </summary>
        public List<RestPredicate> Predicates { get; set; }

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
            Predicates = new();
        }
    }
}
