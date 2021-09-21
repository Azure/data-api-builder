using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Services;

namespace Cosmos.GraphQL.Service.Resolvers
{
    public class SqlMutationEngine : IMutationEngine
    {
        private readonly IMetadataStoreProvider _metadataStoreProvider;

        /// <summary>
        /// Constructor.
        /// </summary>
        public SqlMutationEngine(IMetadataStoreProvider metadataStoreProvider)
        {
            _metadataStoreProvider = metadataStoreProvider;
        }

        /// <summary>
        /// Persists resolver configuration. This is a no-op for MsSql
        /// since the it has been read from a config file.
        /// </summary>
        /// <param name="resolver">The given mutation resolver.</param>
        public void RegisterResolver(MutationResolver resolver)
        {
            // no op
        }

        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="graphQLMutationName">name of the GraphQL mutation query.</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public Task<JsonDocument> Execute(string graphQLMutationName,
            IDictionary<string, object> parameters)
        {
            throw new NotImplementedException("Mutations against Sql Db are not yet supported.");
        }
    }
}
