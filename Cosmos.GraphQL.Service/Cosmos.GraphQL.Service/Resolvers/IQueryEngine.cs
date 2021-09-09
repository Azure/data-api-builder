using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Models;
using GraphQL.Execution;

namespace Cosmos.GraphQL.Services
{
    // <summary>
    // Interface for execution against the backend data source for different queries.
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
        public Task<JsonDocument> ExecuteAsync(string graphQLQueryName, IDictionary<string, ArgumentValue> parameters);

        // <summary>
        // Executes the given named graphql query on the backend and expecting a list of Jsons back.
        // </summary>
        public IEnumerable<JsonDocument> ExecuteList(string graphQLQueryName, IDictionary<string, ArgumentValue> parameters);

        // <summary>
        // Returns if the given query is a list query.
        // </summary>
        public bool IsListQuery(string queryName);
    }
}
