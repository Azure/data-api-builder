namespace Azure.DataGateway.Service.Models
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
        /// <summary>
        /// The operation used in comparison
        /// </summary>
        public PredicateOperation Op { get; set; }
        /// <summary>
        /// Operation used to interact with other comparisons
        /// </summary>
        public LogicalOperation Lop { get; set; }

        /// <summary>
        /// Constructs a new RestPredicate with the provided arguments.
        /// </summary>
        /// <param name="field">The field is what is compared.</param>
        /// <param name="value">The value is what we compare field against.</param>
        /// <param name="op">The operation used to do the comparison.</param>
        /// <param name="lop">The operation used to do logical comparisons.</param>
        /// 
        public RestPredicate(string field = "", string value = "", PredicateOperation op = PredicateOperation.Equal, LogicalOperation lop = LogicalOperation.And)
        {
            Field = field;
            Value = value;
            Op = op;
            Lop = lop;
        }
    }
}
