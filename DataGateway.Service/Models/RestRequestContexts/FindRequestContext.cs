using System.Collections.Generic;
using Azure.DataGateway.Config;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// FindRequestContext provides the major components of a REST query
    /// corresponding to the FindById or FindMany operations.
    /// </summary>
    public class FindRequestContext : RestRequestContext
    {
        /// <summary>
        /// Constructor.
        /// </summary>

        public FindRequestContext(string entityName, string schemaName, string tableName, bool isList, Dictionary<string, string>? mapping = null)
            : base(HttpRestVerbs.GET, entityName, schemaName, tableName, mapping)
        {
            FieldsToBeReturned = new();
            PrimaryKeyValuePairs = new();
            FieldValuePairsInBody = new();
            IsMany = isList;
            OperationType = Operation.Find;
        }
    }
}
