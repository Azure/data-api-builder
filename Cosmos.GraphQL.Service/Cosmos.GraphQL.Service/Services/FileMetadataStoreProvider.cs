using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Services;
using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service
{
    public class FileMetadataStoreProvider : IMetadataStoreProvider
    {
        private string _graphQLSchema;
        private IDictionary<string, string> _resolvers; 

        public FileMetadataStoreProvider()
        {
            init();
        }

        private void init()
        {
            string jsonString = File.ReadAllText(@"config.json");

            using (JsonDocument document = JsonDocument.Parse(jsonString))
            {
                JsonElement root = document.RootElement;
                JsonElement schema = root.GetProperty("GraphQLSchema");
                _graphQLSchema = schema.GetString();
                JsonElement resolversListJson = root.GetProperty("Resolvers");
                _resolvers = new Dictionary<string,string>();
                foreach(JsonElement resolver in resolversListJson.EnumerateArray())
                {
                    _resolvers.Add(resolver.GetProperty("id").ToString(), resolver.ToString());
                }
            }

        }
        /// <summary>
        /// Reads generated JSON configuration file with GraphQL Schema
        /// </summary>
        /// <returns>GraphQL schema as string </returns>
        public string GetGraphQLSchema()
        {
            return _graphQLSchema;
        }

        public MutationResolver GetMutationResolver(string name)
        {
            if (!_resolvers.TryGetValue(name, out string resolver))
            {
                throw new KeyNotFoundException("Mutation Resolver does not exist.");
            }

            return JsonSerializer.Deserialize<MutationResolver>(resolver);
        }

        public GraphQLQueryResolver GetQueryResolver(string name)
        {
            if (!_resolvers.TryGetValue(name, out string resolver))
            {
                throw new KeyNotFoundException("Query Resolver does not exist.");
            }

            return JsonSerializer.Deserialize<GraphQLQueryResolver>(resolver);
        }

        public void StoreGraphQLSchema(string schema)
        {
            // no op
        }

        public void StoreMutationResolver(MutationResolver mutationResolver)
        {
            throw new NotImplementedException();
        }

        public void StoreQueryResolver(GraphQLQueryResolver mutationResolver)
        {
            throw new NotImplementedException();
        }
    }
}
