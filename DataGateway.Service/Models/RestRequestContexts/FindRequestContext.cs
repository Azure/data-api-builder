using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.AspNetCore.Mvc;

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

        public FindRequestContext(string entityName, DatabaseObject dbo, bool isList)
            : base(entityName, dbo)
        {
            FieldsToBeReturned = new();
            PrimaryKeyValuePairs = new();
            FieldValuePairsInBody = new();
            IsMany = isList;
            OperationType = Operation.Find;
        }

        public override async Task<IActionResult> DispatchExecute(IQueryEngine _queryEngine)
        {
            return await _queryEngine.ExecuteAsync(this);
        }
    }
}
