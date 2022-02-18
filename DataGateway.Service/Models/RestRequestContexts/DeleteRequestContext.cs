namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// DeleteRequestContext provides the major components of a REST query
    /// corresponding to the DeleteById or DeleteMany operations.
    /// </summary>
    public class DeleteRequestContext : RestRequestContext
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public DeleteRequestContext(string entityName, bool isList)
            : base(HttpRestVerbs.DELETE, entityName)
        {
            FieldsToBeReturned = new();
            PrimaryKeyValuePairs = new();
            FieldValuePairsInBody = new();
            IsMany = isList;
            OperationType = Operation.Delete;
        }
    }
}
