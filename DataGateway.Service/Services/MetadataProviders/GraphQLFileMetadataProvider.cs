using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using Azure.DataGateway.Service.Models;
using HotChocolate.Types;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// Provides functionality to read GraphQL Schema and resolver config
    /// from text file to make available to GraphQL service.
    /// </summary>
    public class GraphQLFileMetadataProvider : IGraphQLMetadataProvider
    {
        public ResolverConfig GraphQLResolverConfig { get; set; }

        /// <summary>
        /// Stores mutation resolvers contained in configuration file.
        /// </summary>
        private Dictionary<string, MutationResolver> _mutationResolvers;

        public GraphQLFileMetadataProvider(
            IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath)
        {
            RuntimeConfig config = runtimeConfigPath.CurrentValue.ConfigValue!;

            // At this point, the validation is done so, ConfigValue and ResolverConfigFile
            // must not be null.
            string resolverConfigJson =
                File.ReadAllText(config.DataSource.ResolverConfigFile!);

            // Even though the file name may not be null and exist, the check here
            // guarantees it is not empty.
            if (string.IsNullOrEmpty(resolverConfigJson))
            {
                throw new ArgumentNullException("runtime-config.data-source.resolver-config-file",
                    $"The resolver config file contents are empty resolver-config-file: " +
                    $"{config.DataSource.ResolverConfigFile}\n" +
                    $"RuntimeConfigPath: {runtimeConfigPath.CurrentValue.ConfigFileName}");
            }

            GraphQLResolverConfig =
                RuntimeConfig.GetDeserializedConfig<ResolverConfig>(resolverConfigJson);

            if (string.IsNullOrEmpty(GraphQLResolverConfig.GraphQLSchema))
            {
                string graphQLSchema = File.ReadAllText(
                        GraphQLResolverConfig.GraphQLSchemaFile ?? "schema.gql");
                GraphQLResolverConfig = GraphQLResolverConfig with { GraphQLSchema = graphQLSchema };
            }

            if (string.IsNullOrEmpty(GraphQLResolverConfig.GraphQLSchema))
            {
                throw new ArgumentNullException(
                    "hawaii-config.data-source.resolver-config-file.graphql-schema",
                    "GraphQLSchema is required in the resolver-config-file.");
            }

            _mutationResolvers = new();
            foreach (MutationResolver resolver in GraphQLResolverConfig.MutationResolvers)
            {
                _mutationResolvers.Add(resolver.Id, resolver);
            }
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="source">Source to copy from.</param>
        public GraphQLFileMetadataProvider(
            GraphQLFileMetadataProvider source)
        {
            GraphQLResolverConfig = source.GraphQLResolverConfig;
            _mutationResolvers = source._mutationResolvers;
        }

        /// Default Constructor for Mock tests.
        public GraphQLFileMetadataProvider()
        {
            GraphQLResolverConfig = new(string.Empty, string.Empty);
            _mutationResolvers = new();
        }

        /// <summary>
        /// Reads generated JSON configuration file with GraphQL Schema
        /// </summary>
        /// <returns>GraphQL schema as string </returns>
        public string GetGraphQLSchema()
        {
            return GraphQLResolverConfig.GraphQLSchema;
        }

        public MutationResolver GetMutationResolver(string name)
        {
            if (!_mutationResolvers.TryGetValue(name, out MutationResolver? resolver))
            {
                throw new KeyNotFoundException("Mutation Resolver does not exist.");
            }

            return resolver;
        }

        public GraphQLType GetGraphQLType(ObjectType objectType)
        {
            IDirective nameDirective = objectType.Directives.First(d => d.Name == ModelDirectiveType.DirectiveName);

            string nameFromDirective = nameDirective.GetArgument<string>("name");

            if (string.IsNullOrEmpty(nameFromDirective))
            {
                return GetGraphQLType(objectType.Name);
            }

            return GetGraphQLType(nameFromDirective);
        }

        public GraphQLType GetGraphQLType(string name)
        {
            if (!GraphQLResolverConfig.GraphQLTypes.TryGetValue(name, out GraphQLType? typeInfo))
            {
                typeInfo = GraphQLResolverConfig.GraphQLTypes.Values.FirstOrDefault(t => t.Table == name);
                if (typeInfo is null)
                {
                    throw new KeyNotFoundException($"Table Definition for {name} does not exist.");
                }
            }

            return typeInfo;
        }

        public ResolverConfig GetResolvedConfig()
        {
            return GraphQLResolverConfig;
        }
    }
}
