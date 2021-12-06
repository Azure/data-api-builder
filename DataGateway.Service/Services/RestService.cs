using System;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;

namespace Azure.DataGateway.Services
{
    /// <summary>
    /// Service providing REST Api executions.
    /// </summary>
    public class RestService
    {
        private readonly IQueryEngine _queryEngine;
        private readonly IMetadataStoreProvider _metadataStoreProvider;

        public RestService(IQueryEngine queryEngine, IMetadataStoreProvider metadataStoreProvider)
        {
            _queryEngine = queryEngine;
            _metadataStoreProvider = metadataStoreProvider;
        }

        /// <summary>
        /// Invokes the request parser to identify major components of the FindQueryStructure
        /// and executes the find query.
        /// </summary>
        /// <param name="entityName">The entity name.</param>
        /// <param name="primaryKeyRoute">The primary key route. e.g. customerName/Xyz/saleOrderId/123</param>
        /// <param name="queryString">The query string portion of the request. e.g. ?_f=customerName</param>
        public async Task<JsonDocument> ExecuteFindAsync(string entityName, string primaryKeyRoute, string queryString)
        {
            FindRequestContext context = new(entityName, isList: string.IsNullOrEmpty(primaryKeyRoute));
            RequestParser.ParsePrimaryKey(primaryKeyRoute, context);

            if (!string.IsNullOrEmpty(queryString))
            {
                RequestParser.ParseQueryString(System.Web.HttpUtility.ParseQueryString(queryString), context);
            }

            if(!RequestValidator.IsValidFindRequest(context, _metadataStoreProvider))
            {
                throw new ArgumentException(message:"Invalid Primary Key usage");
            }

            return await _queryEngine.ExecuteAsync(context);
        }
    }
}
