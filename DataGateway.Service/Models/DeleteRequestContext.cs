namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// FindRequestContext provides the major components of a REST query
    /// corresponding to the FindById or FindMany operations.
    /// </summary>
    public class DeleteRequestContext : RestRequestContext
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public DeleteRequestContext(string entityName, bool isList)
        {
            EntityName = entityName;
            FieldsToBeReturned = new();
            PrimaryKeyValuePairs = new();
            FieldValuePairsInBody = new();
            IsMany = isList;
            HttpVerb = HttpRestVerbs.DELETE;
            OperationType = Operation.Delete;
        }
    }
}
