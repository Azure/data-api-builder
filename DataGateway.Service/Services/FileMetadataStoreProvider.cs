using Azure.DataGateway.Service.configurations;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Azure.DataGateway.Service
{

    public class ResolverConfig
    {
        /// <summary>
        /// String Representation of graphQL schema, non escaped.
        /// </summary>
        public string GraphQLSchema { get; set; }

        /// <summary>
        /// Location of the graphQL schema file
        /// </summary>
        public string GraphQLSchemaFile { get; set; }
        public List<GraphQLQueryResolver> QueryResolvers { get; set; }
        public List<MutationResolver> MutationResolvers { get; set; }
    }

    /// <summary>
    /// Reads GraphQL Schema and resolver config from text files to make available to GraphQL service.
    /// </summary>
    public class FileMetadataStoreProvider : IMetadataStoreProvider
    {
        private ResolverConfig _config;

        /// <summary>
        /// Stores query resolvers contained in configuration file.
        /// </summary>
        private Dictionary<string, GraphQLQueryResolver> _queryResolvers;

        /// <summary>
        /// Stores mutation resolvers contained in configuration file.
        /// </summary>
        private Dictionary<string, MutationResolver> _mutationResolvers;

        private readonly DataGatewayConfig _dataGatewayConfig;

        public FileMetadataStoreProvider(IOptions<DataGatewayConfig> dataGatewayConfig)
        {
            _dataGatewayConfig = dataGatewayConfig.Value;
            Init();
        }

        private void Init()
        {
            string jsonString = File.ReadAllText(
                    _dataGatewayConfig.ResolverConfigFile);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            _config = JsonSerializer.Deserialize<ResolverConfig>(jsonString, options);

            if (string.IsNullOrEmpty(_config.GraphQLSchema))
            {
                _config.GraphQLSchema = File.ReadAllText(_config.GraphQLSchemaFile ?? "schema.gql");
            }

            _config.QueryResolvers ??= new();
            _config.MutationResolvers ??= new();

            _queryResolvers = new();
            foreach (GraphQLQueryResolver resolver in _config.QueryResolvers)
            {
                _queryResolvers.Add(resolver.Id, resolver);
            }

            _mutationResolvers = new();
            foreach (MutationResolver resolver in _config.MutationResolvers)
            {
                _mutationResolvers.Add(resolver.Id, resolver);
            }
        }
        /// <summary>
        /// Reads generated JSON configuration file with GraphQL Schema
        /// </summary>
        /// <returns>GraphQL schema as string </returns>
        public string GetGraphQLSchema()
        {
            return _config.GraphQLSchema;
        }

        public MutationResolver GetMutationResolver(string name)
        {
            if (!_mutationResolvers.TryGetValue(name, out MutationResolver resolver))
            {
                throw new KeyNotFoundException("Mutation Resolver does not exist.");
            }

            return resolver;
        }

        public GraphQLQueryResolver GetQueryResolver(string name)
        {
            if (!_queryResolvers.TryGetValue(name, out GraphQLQueryResolver resolver))
            {
                throw new KeyNotFoundException("Query Resolver does not exist.");
            }

            return resolver;
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
