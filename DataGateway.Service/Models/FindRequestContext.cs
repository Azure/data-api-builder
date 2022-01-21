namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// FindRequestContext provides the major components of a REST query
    /// corresponding to the FindById or FindMany operations.
    /// </summary>
    public class FindRequestContext : RequestContext
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public FindRequestContext(string entityName, bool isList)
        {
            EntityName = entityName;
            FieldsToBeReturned = new();
            PrimaryKeyValuePairs = new();
            IsMany = isList;
            HttpVerb = HttpRestVerbs.GET;
            OperationType = Operation.Find;
        }
    }
}
