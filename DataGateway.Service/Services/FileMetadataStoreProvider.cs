using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Services;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service
{
    /// <summary>
    /// A class describing the format of the JSON resolver configuration file.
    /// </summary>
    /// <param name="GraphQLSchema">String Representation of graphQL schema, non escaped. This has higher priority than GraphQLSchemaFile, so if both are set this one will be used.</param>
    /// <param name="GraphQLSchemaFile">Location of the graphQL schema file</param>
    /// <param name="DatabaseSchema">A JSON encoded version of the information that resolvers need about schema of the schema of the database.</param>
    public record ResolverConfig(string GraphQLSchema, string GraphQLSchemaFile, DatabaseSchema DatabaseSchema)
    {
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
        public Dictionary<string, GraphQLType> GraphQLTypes { get; set; } = new();
    }

    /// <summary>
    /// Reads GraphQL Schema and resolver config from text files to make available to GraphQL service.
    /// </summary>
    public class FileMetadataStoreProvider : IMetadataStoreProvider
    {
        private readonly ResolverConfig _config;
        private readonly FilterParser? _filterParser;

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

            // This feels verbose but it avoids having to make _config nullable - which would result in more
            // down the line issues and null check requirements
            ResolverConfig? deserializedConfig;
            if ((deserializedConfig = JsonSerializer.Deserialize<ResolverConfig>(jsonString, options)) == null)
            {
                throw new JsonException("Failed to get a ResolverConfig from the provided config");
            }
            else
            {
                _config = deserializedConfig;
            }

            if (string.IsNullOrEmpty(_config.GraphQLSchema))
            {
                _config = _config with { GraphQLSchema = File.ReadAllText(_config.GraphQLSchemaFile ?? "schema.gql") };
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

            if (_config.DatabaseSchema != default)
            {
                _filterParser = new(_config.DatabaseSchema);
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
            if (!_mutationResolvers.TryGetValue(name, out MutationResolver? resolver))
            {
                throw new KeyNotFoundException("Mutation Resolver does not exist.");
            }

            return resolver;
        }

        public GraphQLQueryResolver GetQueryResolver(string name)
        {
            if (!_queryResolvers.TryGetValue(name, out GraphQLQueryResolver? resolver))
            {
                throw new KeyNotFoundException("Query Resolver does not exist.");
            }

            return resolver;
        }

        public TableDefinition GetTableDefinition(string name)
        {
            if (!_config.DatabaseSchema.Tables.TryGetValue(name, out TableDefinition? metadata))
            {
                throw new KeyNotFoundException($"Table Definition for {name} does not exist.");
            }

            return metadata;
        }

        public GraphQLType GetGraphQLType(string name)
        {
            if (!_config.GraphQLTypes.TryGetValue(name, out GraphQLType? typeInfo))
            {
                throw new KeyNotFoundException($"Table Definition for {name} does not exist.");
            }

            return typeInfo;
        }

        public ResolverConfig GetResolvedConfig()
        {
            return _config;
        }

        public FilterParser GetFilterParser()
        {
            if (_filterParser == null)
            {
                throw new InvalidOperationException("No filter parser has been initialised");
            }

            return _filterParser;
        }
    }
}
