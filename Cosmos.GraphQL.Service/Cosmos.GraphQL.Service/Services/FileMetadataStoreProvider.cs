using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Services;
using Cosmos.GraphQL.Service.configurations;
using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace Cosmos.GraphQL.Service
{
    /// <summary>
    /// Reads GraphQL Schema and resolver config from text files to make available to GraphQL service.
    /// </summary>
    public class FileMetadataStoreProvider : IMetadataStoreProvider
    {
        /// <summary>
        /// String Representation of graphQL schema, non escaped.
        /// </summary>
        private string _graphQLSchema;

        /// <summary>
        /// Stores resolvers contained in configuration file.
        /// </summary>
        private IDictionary<string, string> _resolvers;

        private readonly DataGatewayConfig _databaseConnection;

        public FileMetadataStoreProvider(DataGatewayConfig databaseConnection)
        {
            _databaseConnection = databaseConnection;
            init();
        }

        private void init()
        {
            string jsonString = File.ReadAllText(
                    _databaseConnection.ResolverConfigFile);

            using (JsonDocument document = JsonDocument.Parse(jsonString))
            {
                JsonElement root = document.RootElement;
                JsonElement schema = root.GetProperty("GraphQLSchema");

                if (string.IsNullOrEmpty(schema.GetString()))
                {
                    _graphQLSchema = File.ReadAllText("schema.gql");
                }
                else
                {
                    _graphQLSchema = schema.GetString();
                }

                JsonElement resolversListJson = root.GetProperty("Resolvers");
                _resolvers = new Dictionary<string, string>();
                foreach (JsonElement resolver in resolversListJson.EnumerateArray())
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
