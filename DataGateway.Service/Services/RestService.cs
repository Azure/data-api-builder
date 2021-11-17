using Azure.DataGateway.Service.Resolvers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Azure.DataGateway.Services
{
    /// <summary>
    /// Service providing REST Api executions.
    /// </summary>
    public class RestService
    {
        private readonly IQueryEngine _queryEngine;

        public RestService(IQueryEngine queryEngine)
        {
            _queryEngine = queryEngine;
        }

        /// <summary>
        /// Invokes the request parser to identify major components of the FindQueryStructure
        /// and executes the find query.
        /// </summary>
        /// <param name="entityName">The entity name.</param>
        /// <param name="queryByPrimaryKey">The primary key route. e.g. customerName/Xyz/saleOrderId/123</param>
        /// <param name="queryString">The query string portion of the request. e.g. ?_f=customerName</param>
        internal async Task<JsonDocument> ExecuteFindAsync(string entityName, string queryByPrimaryKey, string queryString)
        {
            FindQueryStructure queryStructure = new(entityName, isList: string.IsNullOrEmpty(queryByPrimaryKey));
            RequestParser.ParsePrimaryKey(queryByPrimaryKey, queryStructure);
            RequestParser.ParseQueryString(System.Web.HttpUtility.ParseQueryString(queryString), queryStructure);
            return await _queryEngine.ExecuteAsync(queryStructure);
        }

    }
}
