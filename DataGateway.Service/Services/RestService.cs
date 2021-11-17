using Azure.DataGateway.Service.Resolvers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Azure.DataGateway.Services
{
    public class RestService
    {
        private readonly IQueryEngine _queryEngine;

        public RestService(IQueryEngine queryEngine)
        {
            _queryEngine = queryEngine;
        }

        internal async Task<JsonDocument> ExecuteAsync(string entityName, string queryByPrimaryKey, string queryString)
        {
            FindQueryStructure queryStructure = new(entityName, isList: string.IsNullOrEmpty(queryByPrimaryKey));
            RequestParser.ParsePrimaryKey(queryByPrimaryKey, queryStructure);
            RequestParser.ParseQueryString(System.Web.HttpUtility.ParseQueryString(queryString), queryStructure);
            return await _queryEngine.ExecuteAsync(queryStructure);
        }

    }
}
