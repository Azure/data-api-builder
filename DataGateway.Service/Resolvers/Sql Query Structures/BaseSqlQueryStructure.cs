using System.Collections.Generic;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Resolvers
{

    /// <summary>
    /// Holds shared properties and methods among
    /// Sql*QueryStructure classes
    /// </summary>
    public abstract class BaseSqlQueryStructure
    {
        /// <summary>
        /// The name of the main table to be queried.
        /// </summary>
        public string TableName { get; protected set; }
        /// <summary>
        /// The alias of the main table to be queried.
        /// </summary>
        public string TableAlias { get; protected set; }
        /// <summary>
        /// The columns which the query selects
        /// </summary>
        public List<LabelledColumn> Columns { get; }
        /// <summary>
        /// Predicates that should filter the result set of the query.
        /// </summary>
        public List<Predicate> Predicates { get; }
        /// <summary>
        /// Parameters values required to execute the query.
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }
        /// <summary>
        /// Counter.Next() can be used to get a unique integer within this
        /// query, which can be used to create unique aliases, parameters or
        /// other identifiers.
        /// </summary>
        public IncrementingInteger Counter { get; }

        public BaseSqlQueryStructure(IncrementingInteger counter = null)
        {
            Columns = new();
            Predicates = new();
            Parameters = new();
            Counter = counter ?? new IncrementingInteger();
        }

        /// <summary>
        ///  Add parameter to Parameters and return the name associated with it
        /// </summary>
        protected string MakeParamWithValue(object value)
        {
            string paramName = $"param{Counter.Next()}";
            Parameters.Add(paramName, value);
            return paramName;
        }
    }
}
