using System;
using CommandLine;

namespace Hawaii.Cli.Models
{
    /// <summary>
    /// Contains different options that are supported by this Cli tool.
    /// </summary>
    public sealed class CommandLineOptions
    {
        [Value(0, Required = false, HelpText = "Specify the command - init/add/update.")]
        public string? Command { get; set; }

        [Value(1, Required = false, HelpText = "Specify the name of entity for adding or updating an entity.")]
        public string? Entity { get; set; }

        [Option('n', "name", Required = false, HelpText = "Specify the file name, Default value = hawaii-config.")]
        public string? Name { get; set; }

        [Option("database-type", Required = false, HelpText = "Type of database to connect.")]
        public string? DatabaseType { get; set; }

        [Option("connection-string", Required = false, HelpText = "Connection details to connect to database.")]
        public string? ConnectionString { get; set; }

        [Option("resolver-config-file", Required = false, HelpText = "Path of the file to resolve the configuration for CosmosDB.")]
        public string? ResolverConfigFile { get; set; }

        [Option("host-mode", Required = false, HelpText = "Specify the Host mode - Development/Production. Default value = Production")]
        public string? HostMode { get; set; }

        //TODO: Link options with Specidied commands
        // we need to make sure certain options are only required with certain commands.
        // for example: source is required only with add/update and not init

        [Option('s', "source", Required = false, HelpText = "Name of the table.")]
        public string? Source { get; set; }

        [Option("rest", Required = false, HelpText = "Route for rest api.")]
        public string? RestRoute { get; set; }

        [Option("graphql", Required = false, HelpText = "Type of graphQL.")]
        public string? GraphQLType { get; set; }

        [Option("permission", Required = false, HelpText = "Permission required to acess source table.")]
        public string? Permission { get; set; }

        [Option("fields.include", Required = false, HelpText = "Fields that are allowed access to permission.")]
        public string? FieldsToInclude { get; set; }

        [Option("fields.exclude", Required = false, HelpText = "Fields that are excluded from the action lists.")]
        public string? FieldsToExclude { get; set; }

        [Option("relationship", Required = false, HelpText = "Specify relationship between two entities.")]
        public string? Relationship { get; set; }

        [Option("cardinality", Required = false, HelpText = "Specify cardinality between two entities.")]
        public string? Cardinality { get; set; }

        [Option("target.entity", Required = false, HelpText = "Another exposed entity to which the source entity relates to.")]
        public string? TargetEntity { get; set; }

        [Option("linking.object", Required = false, HelpText = "Database object that is used to support an M:N relationship.")]
        public string? LinkingObject { get; set; }

        [Option("linking.source.fields", Required = false, HelpText = "Database fields in the linking object to connect to the related item in the source entity.")]
        public string? LinkingSourceFields { get; set; }

        [Option("linking.target.fields", Required = false, HelpText = "Database fields in the linking object to connect to the related item in the target entity.")]
        public string? LinkingTargetFields { get; set; }

        [Option("mapping.fields", Required = false, HelpText = "Specify fields to be used for mapping the entities.")]
        public string? MappingFields { get; set; }

    }
}
