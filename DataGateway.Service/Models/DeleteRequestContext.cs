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
        {
            EntityName = entityName;
            FieldsToBeReturned = new();
            PrimaryKeyValuePairs = new();
            FieldValuePairsInBody = new();
            FieldValuePairsInUrl = new();
            IsMany = isList;
            HttpVerb = HttpRestVerbs.DELETE;
            OperationType = Operation.Delete;
        }
    }
}
