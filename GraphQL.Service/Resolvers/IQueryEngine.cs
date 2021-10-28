using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.GraphQL.Service.Models;

namespace Microsoft.GraphQL.Services
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
        public Task<JsonDocument> ExecuteAsync(string graphQLQueryName, IDictionary<string, object> parameters);

        // <summary>
        // Executes the given named graphql query on the backend and expecting a list of Jsons back.
        // </summary>
        public Task<IEnumerable<JsonDocument>> ExecuteListAsync(string graphQLQueryName, IDictionary<string, object> parameters);
    }
}
