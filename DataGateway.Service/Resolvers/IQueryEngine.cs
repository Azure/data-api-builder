using Azure.DataGateway.Service.Models;
using HotChocolate.Resolvers;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Azure.DataGateway.Services
{
    // <summary>
    // Interface for execution of GraphQL queries against a database.
    // </summary>
    public interface IQueryEngine
    {
        // <summary>
        // Registers the given resolver with this query engine.
        // </summary>
        public void RegisterResolver(GraphQLQueryResolver resolver);

        // <summary>
        // Executes the given named graphql query on the backend and expecting a single Json back.
        // </summary>
        public Task<JsonDocument> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object> parameters);

        // <summary>
        // Executes the given named graphql query on the backend and expecting a list of Jsons back.
        // </summary>
        public Task<IEnumerable<JsonDocument>> ExecuteListAsync(IMiddlewareContext context, IDictionary<string, object> parameters);
    }
}
