using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.configurations;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Service.Resolvers;
using GraphQL.Execution;
using Microsoft.Azure.Cosmos;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Services
{
    //<summary>
    // CosmosQueryEngine to ExecuteAsync against CosmosDb.
    //</summary>
    public class CosmosQueryEngine : IQueryEngine
    {
        private readonly CosmosClientProvider _clientProvider;
        private IMetadataStoreProvider _metadataStoreProvider;

        // <summary>
        // Constructor.
        // </summary>
        public CosmosQueryEngine(IClientProvider<CosmosClient> clientProvider, IMetadataStoreProvider metadataStoreProvider)
        {
            this._clientProvider = (CosmosClientProvider)clientProvider;
            this._metadataStoreProvider = metadataStoreProvider;
        }

        // <summary>
        // Register the given resolver with this query engine.
        // </summary>
        public void RegisterResolver(GraphQLQueryResolver resolver)
        {
            this._metadataStoreProvider.StoreQueryResolver(resolver);
        }

        // <summary>
        // ExecuteAsync the given named graphql query on the backend.
        // </summary>
        public async Task<JsonDocument> ExecuteAsync(string graphQLQueryName, IDictionary<string, ArgumentValue> parameters)
        {
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            var resolver = _metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            var container = this._clientProvider.getCosmosClient().GetDatabase(resolver.databaseName).GetContainer(resolver.containerName);
            var querySpec = new QueryDefinition(resolver.parametrizedQuery);

            if (parameters != null)
            {
                foreach (var parameterEntry in parameters)
                {
                    querySpec.WithParameter("@" + parameterEntry.Key, parameterEntry.Value.Value);
                }
            }

            var firstPage = container.GetItemQueryIterator<JObject>(querySpec).ReadNextAsync().Result;

            JObject firstItem = null;

            var iterator = firstPage.GetEnumerator();

            while (iterator.MoveNext() && firstItem == null)
            {
                firstItem = iterator.Current;
            }
            JsonDocument jsonDocument = JsonDocument.Parse(firstItem.ToString());

            return jsonDocument;
        }

        public IEnumerable<JsonDocument> ExecuteList(string graphQLQueryName, IDictionary<string, ArgumentValue> parameters)
        {
            // TODO: add support for nesting
            // TODO: add support for join query against another container
            // TODO: add support for TOP and Order-by push-down

            var resolver = _metadataStoreProvider.GetQueryResolver(graphQLQueryName);
            var container = this._clientProvider.getCosmosClient().GetDatabase(resolver.databaseName).GetContainer(resolver.containerName);
            var querySpec = new QueryDefinition(resolver.parametrizedQuery);

            if (parameters != null)
            {
                foreach (var parameterEntry in parameters)
                {
                    querySpec.WithParameter("@" + parameterEntry.Key, parameterEntry.Value.Value);
                }
            }

            var firstPage = container.GetItemQueryIterator<JObject>(querySpec).ReadNextAsync().Result;

            JObject firstItem = null;

            var iterator = firstPage.GetEnumerator();

            List<JsonDocument> resultsAsList = new List<JsonDocument>();
            while (iterator.MoveNext())
            {
                firstItem = iterator.Current;
                resultsAsList.Add(JsonDocument.Parse(firstItem.ToString()));
            }

            return resultsAsList;
        }

        // <summary>
        // Returns if the given query is a list query.
        // </summary>
        public bool IsListQuery(string queryName)
        {
            return _metadataStoreProvider.GetQueryResolver(queryName).isList;
        }
    }
}