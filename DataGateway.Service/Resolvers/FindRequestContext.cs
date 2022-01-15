namespace Azure.DataGateway.Service.Resolvers
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
            Fields = new();
            FieldValuePairs = new();

            IsMany = isList;
            // OperationType = 
        }
    }
}
