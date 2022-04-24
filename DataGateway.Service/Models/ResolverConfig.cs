using System.Collections.Generic;
using Azure.DataGateway.Config;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// A class describing the format of the JSON resolver configuration file.
    /// </summary>
    /// <param name="GraphQLSchema">String Representation of graphQL schema, non escaped. This has higher priority than GraphQLSchemaFile, so if both are set this one will be used.</param>
    /// <param name="GraphQLSchemaFile">Location of the graphQL schema file</param>
    public record ResolverConfig(string GraphQLSchema, string GraphQLSchemaFile)
    {
        /// <summary>
        /// A list containing metadata required to execute the different
        /// mutations in the GraphQL schema. See MutationResolver for details.
        /// </summary>
        public List<MutationResolver> MutationResolvers { get; set; } = new();

        /// <summary>
        /// A list containing metadata required to resolve the different
        /// types in the GraphQL schema. See GraphQLType for details.
        /// </summary>
        public Dictionary<string, GraphQLType> GraphQLTypes { get; set; } = new();

        /// <summary>
        /// A JSON encoded version of the information that resolvers
        /// need about schema of the database.
        /// </summary>
        public DatabaseSchema? DatabaseSchema { get; set; }
    }
}
