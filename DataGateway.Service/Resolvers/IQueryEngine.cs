using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using HotChocolate.Resolvers;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Interface for execution of queries against a database.
    /// </summary>
    public interface IQueryEngine
    {
        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL query and
        /// expecting a single Json back.
        /// </summary>
        public Task<JsonDocument> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object> parameters, bool isPaginationQuery);

        /// <summary>
        /// Executes the given IMiddlewareContext of the GraphQL and expecting a
        /// list of Jsons back.
        /// </summary>
        public Task<IEnumerable<JsonDocument>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object> parameters);

        /// <summary>
        /// Given the FindQueryContext structure, obtains the query text and executes it against the backend.
        /// </summary>
        public Task<JsonDocument> ExecuteAsync(FindRequestContext queryStructure);
    }
}
