// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CommandLine;

namespace Cli.Commands
{
    /// <summary>
    /// Command options for entity manipulation.
    /// </summary>
    public class EntityOptions : Options
    {
        public EntityOptions(
            string entity,
            string? sourceType,
            IEnumerable<string>? sourceParameters,
            IEnumerable<string>? sourceKeyFields,
            string? restRoute,
            IEnumerable<string>? restMethodsForStoredProcedure,
            string? graphQLType,
            string? graphQLOperationForStoredProcedure,
            IEnumerable<string>? fieldsToInclude,
            IEnumerable<string>? fieldsToExclude,
            string? policyRequest,
            string? policyDatabase,
            string? cacheEnabled,
            string? cacheTtl,
            string? config)
            : base(config)
        {
            Entity = entity;
            SourceType = sourceType;
            SourceParameters = sourceParameters;
            SourceKeyFields = sourceKeyFields;
            RestRoute = restRoute;
            RestMethodsForStoredProcedure = restMethodsForStoredProcedure;
            GraphQLType = graphQLType;
            GraphQLOperationForStoredProcedure = graphQLOperationForStoredProcedure;
            FieldsToInclude = fieldsToInclude;
            FieldsToExclude = fieldsToExclude;
            PolicyRequest = policyRequest;
            PolicyDatabase = policyDatabase;
            CacheEnabled = cacheEnabled;
            CacheTtl = cacheTtl;
        }

        // Entity is required but we have made required as false to have custom error message (more user friendly), if not provided.
        [Value(0, MetaName = "Entity", Required = false, HelpText = "Name of the entity.")]
        public string Entity { get; }

        [Option("source.type", Required = false, HelpText = "Type of the database object.Must be one of: [table, view, stored-procedure]")]
        public string? SourceType { get; }

        [Option("source.params", Required = false, Separator = ',', HelpText = "Dictionary of parameters and their values for Source object.\"param1:val1,param2:value2,..\"")]
        public IEnumerable<string>? SourceParameters { get; }

        [Option("source.key-fields", Required = false, Separator = ',', HelpText = "The field(s) to be used as primary keys.")]
        public IEnumerable<string>? SourceKeyFields { get; }

        [Option("rest", Required = false, HelpText = "Route for rest api.")]
        public string? RestRoute { get; }

        [Option("rest.methods", Required = false, Separator = ',', HelpText = "HTTP actions to be supported for stored procedure. Specify the actions as a comma separated list. Valid HTTP actions are : [GET, POST, PUT, PATCH, DELETE]")]
        public IEnumerable<string>? RestMethodsForStoredProcedure { get; }

        [Option("graphql", Required = false, HelpText = "Type of graphQL.")]
        public string? GraphQLType { get; }

        [Option("graphql.operation", Required = false, HelpText = $"GraphQL operation to be supported for stored procedure. Valid operations are : [Query, Mutation] ")]
        public string? GraphQLOperationForStoredProcedure { get; }

        [Option("fields.include", Required = false, Separator = ',', HelpText = "Fields that are allowed access to permission.")]
        public IEnumerable<string>? FieldsToInclude { get; }

        [Option("fields.exclude", Required = false, Separator = ',', HelpText = "Fields that are excluded from the action lists.")]
        public IEnumerable<string>? FieldsToExclude { get; }

        [Option("policy-request", Required = false, HelpText = "Specify the rule to be checked before sending any request to the database.")]
        public string? PolicyRequest { get; }

        [Option("policy-database", Required = false, HelpText = "Specify an OData style filter rule that will be injected in the query sent to the database.")]
        public string? PolicyDatabase { get; }

        [Option("cache.enabled", Required = false, HelpText = "Specify if caching is enabled for Entity, default value is false.")]
        public string? CacheEnabled { get; }

        [Option("cache.ttl", Required = false, HelpText = "Specify time to live in seconds for cache entries for Entity.")]
        public string? CacheTtl { get; }
    }
}
