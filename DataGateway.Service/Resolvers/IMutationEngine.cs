using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using HotChocolate.Resolvers;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Interface for execution of GraphQL mutations against a database.
    /// </summary>
    public interface IMutationEngine
    {
        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">Middleware context of the mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public Task<JsonDocument> ExecuteAsync(IMiddlewareContext context,
            IDictionary<string, object> parameters);

        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">Middleware context of the mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result and its related pagination metadata</returns>
        public Task<Tuple<JsonDocument, PaginationMetadata>> ExecuteAsyncWithMetadata(IMiddlewareContext context, IDictionary<string, object> parameters);
    }
}
