using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Implements the mutation engine interface for mutations against Sql like databases.
    /// </summary>
    public class SqlMutationEngine : IMutationEngine
    {
        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="graphQLMutationName">name of the GraphQL mutation query.</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public Task<JsonDocument> ExecuteAsync(string graphQLMutationName,
            IDictionary<string, object> parameters)
        {
            throw new NotImplementedException("Mutations against Sql like databases are not yet supported.");
        }
    }
}
