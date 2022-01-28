using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service
{

    /// <summary>
    /// A class describing the format of the JSON resolver configuration file.
    /// </summary>
    public class ResolverConfig
    {
        /// <summary>
        /// String Representation of graphQL schema, non escaped. This has
        /// higher priority than GraphQLSchemaFile, so if both are set this one
        /// will be used.
        /// </summary>
        public string GraphQLSchema { get; set; }

        /// <summary>
        /// Location of the graphQL schema file
        /// </summary>
        public string GraphQLSchemaFile { get; set; }

        /// <summary>
        /// A list containing metadata required to resolve the different
        /// queries in the GraphQL schema. See GraphQLQueryResolver for details.
        /// </summary>
        public List<GraphQLQueryResolver> QueryResolvers { get; set; } = new();

        /// <summary>
        /// A list containing metadata required to execute the different
        /// mutations in the GraphQL schema. See MutationResolver for details.
        /// </summary>
        public List<MutationResolver> MutationResolvers { get; set; } = new();

        /// <summary>
        /// A list containing metadata required to resolve the different
        /// types in the GraphQL schema. See GraphqlType for details.
        /// </summary>
        public Dictionary<string, GraphqlType> GraphqlTypes { get; set; } = new();

        /// <summary>
        /// A JSON encoded version of the information that resolvers need about
        /// schema of the schema of the database.
        /// </summary>
        public DatabaseSchema DatabaseSchema { get; set; }
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

        public FileMetadataStoreProvider(IOptions<DataGatewayConfig> dataGatewayConfig)
        : this(dataGatewayConfig.Value.ResolverConfigFile) { }

        public FileMetadataStoreProvider(string resolverConfigPath)
        {
            string jsonString = File.ReadAllText(resolverConfigPath);
            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true,
            };
            options.Converters.Add(new JsonStringEnumConverter());

            _config = JsonSerializer.Deserialize<ResolverConfig>(jsonString, options);

            if (string.IsNullOrEmpty(_config.GraphQLSchema))
            {
                _config.GraphQLSchema = File.ReadAllText(_config.GraphQLSchemaFile ?? "schema.gql");
            }

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

        public TableDefinition GetTableDefinition(string name)
        {
            if (!_config.DatabaseSchema.Tables.TryGetValue(name, out TableDefinition metadata))
            {
                throw new KeyNotFoundException($"Table Definition for {name} does not exist.");
            }

            return metadata;
        }

        public GraphqlType GetGraphqlType(string name)
        {
            if (!_config.GraphqlTypes.TryGetValue(name, out GraphqlType typeInfo))
            {
                throw new KeyNotFoundException($"Table Definition for {name} does not exist.");
            }

            return typeInfo;
        }

        public ResolverConfig GetResolvedConfig()
        {
            return _config;
        }
    }
}
