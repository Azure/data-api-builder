using Azure.DataGateway.Config;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hawaii.Cli.Classes
{
    // public class DataSource
    // {
    //     public string database_type = "";
    //     public string connection_string = "";
    // }

    // public class Permission
    // {
    //     public string role = "";
    //     public string actions = "";

    //     public Permission(string role, string actions)
    //     {
    //         this.role = role;
    //         this.actions = actions;
    //     }
    // }

    // public record Relationship(
    //     Cardinality Cardinality,
    //     [property: JsonPropertyName("target.entity")]
    //     string TargetEntity,
    //     [property: JsonPropertyName("source.fields")]
    //     string[]? SourceFields,
    //     [property: JsonPropertyName("target.fields")]
    //     string[]? TargetFields,
    //     [property: JsonPropertyName("linking.object")]
    //     string? LinkingObject,
    //     [property: JsonPropertyName("linking.source.fields")]
    //     string[]? LinkingSourceFields,
    //     [property: JsonPropertyName("linking.target.fields")]
    //     string[]? LinkingTargetFields);

    // /// <summary>
    // /// Kinds of relationship cardinality.
    // /// This only represents the right (target, e.g. books) side of the relationship
    // /// when viewing the enclosing entity as the left (source, e.g. publisher) side.
    // /// e.g. publisher can publish "Many" books.
    // /// To get the cardinality of the other side, the runtime needs to flip the sides
    // /// and find the cardinality of the original source (e.g. publisher)
    // /// is with respect to the original target (e.g. books):
    // /// e.g. book can have only "One" publisher.
    // /// Hence, its a Many-To-One relationship from publisher-books
    // /// i.e. a One-Many relationship from books-publisher.
    // /// The various combinations of relationships this leads to are:
    // /// (1) One-To-One (2) Many-One (3) One-To-Many (4) Many-To-Many.
    // /// </summary>
    // public enum Cardinality
    // {
    //     One,
    //     Many
    // }

    // public record PermissionSetting(
    //     string Role,
    //     object[] Actions);

    // public record Entity(
    //     object Source,
    //     object? Rest,
    //     object? GraphQL,
    //     PermissionSetting[] Permissions,
    //     Dictionary<string, Relationship>? Relationships,
    //     Dictionary<string, string>? Mappings)
    // {
    //     /// <summary>
    //     /// Gets the name of the underlying source database object.
    //     /// </summary>
    //     public string GetSourceName()
    //     {
    //         if (((JsonElement)Source).ValueKind is JsonValueKind.String)
    //         {
    //             return JsonSerializer.Deserialize<string>((JsonElement)Source)!;
    //         }
    //         else
    //         {
    //             DatabaseObjectSource objectSource
    //                 = JsonSerializer.Deserialize<DatabaseObjectSource>((JsonElement)Source)!;
    //             return objectSource.Name;
    //         }
    //     }
    // }

    // /// <summary>
    // /// Describes the type, name and parameters for a
    // /// database object source. Useful for more complex sources like stored procedures.
    // /// </summary>
    // /// <param name="Type">Type of the database object.</param>
    // /// <param name="Name">The name of the database object.</param>
    // /// <param name="Parameters">The Parameters to be used for constructing this object
    // /// in case its a stored procedure.</param>
    // public record DatabaseObjectSource(
    //     string Type,
    //     [property: JsonPropertyName("object")] string Name,
    //     string[]? Parameters);

    // /// <summary>
    // /// Describes the REST settings specific to an entity.
    // /// </summary>
    // /// <param name="Route">Instructs the runtime to use this route as the path
    // /// at which the REST endpoint for this entity is exposed
    // /// instead of using the entity-name. Can be a string or Singular-Plural type.
    // /// If string, a corresponding plural route will be added as per the rules at
    // /// <href="https://engdic.org/singular-and-plural-noun-rules-definitions-examples/" /></param>
    // public record RestEntitySettings(object Route);

    // /// <summary>
    // /// Describes the GraphQL settings specific to an entity.
    // /// </summary>
    // /// <param name="Type">Defines the name of the GraphQL type
    // /// that will be used for this entity.Can be a string or Singular-Plural type.
    // /// If string, a default plural route will be added as per the rules at
    // /// <href="https://engdic.org/singular-and-plural-noun-rules-definitions-examples/" /></param>
    // public record GraphQLEntitySettings(object Type);

    // /// <summary>
    // /// Defines a name or route as singular (required) or
    // /// plural (optional).
    // /// </summary>
    // /// <param name="Singular">Singular form of the name.</param>
    // /// <param name="Plural">Optional pluralized form of the name.
    // /// If plural is not specified, a default plural name will be used as per the rules at
    // /// <href="https://engdic.org/singular-and-plural-noun-rules-definitions-examples/" /></param>
    // public record SingularPlural(string Singular, string Plural);

    // public record AuthenticationConfig(
    //     string Provider = "EasyAuth",
    //     Jwt? Jwt = null);

    // /// <summary>
    // /// Settings useful for validating the received Json Web Token (JWT).
    // /// </summary> 
    // /// <param name="Audience"></param>
    // /// <param name="Issuer"></param>
    // /// <param name="IssuerKey"></param>
    // public record Jwt(string Audience, string Issuer, string IssuerKey);

    // /// <summary>
    // /// Indicates the settings are globally applicable.
    // /// </summary>
    // public record GlobalSettings;

    // /// <summary>
    // /// Indicates the settings are for the all the APIs.
    // /// </summary>
    // /// <param name="Enabled">If the API is enabled.</param>
    // /// <param name="Path">The URL path at which the API is available.</param>
    // public record ApiSettings
    //     (bool Enabled = true, string Path = "")
    //     : GlobalSettings();

    // /// <summary>
    // /// Holds the global settings used at runtime for REST Apis.
    // /// </summary>
    // /// <param name="Enabled">If REST endpoints are enabled.
    // /// If set to false, no REST endpoint will be exposed.
    // /// If set to true, REST endpoint will be exposed
    // /// unless the rest property within an entity configuration is set to false.</param>
    // /// <param name="Path">The URL prefix path at which endpoints
    // /// for all entities will be exposed.</param>
    // public record RestGlobalSettings
    //     (bool Enabled = true,
    //      string Path = "/api")
    //     : ApiSettings(Enabled, Path);

    // /// <summary>
    // /// Holds the global settings used at runtime for GraphQL.
    // /// </summary>
    // /// <param name="Enabled">If the GraphQL endpoint is enabled.
    // /// If set to true, the defined GraphQL entities will be exposed unless
    // /// the GraphQL property within an entity configuration is set to false.</param>
    // /// <param name="Path">The URL path at which the graphql endpoint will be exposed.</param>
    // /// <param name="AllowIntrospection">Defines if the GraphQL introspection file
    // /// will be generated by the runtime. If GraphQL is disabled, this will be ignored.</param>
    // public record GraphQLGlobalSettings
    //     (bool Enabled = true,
    //      string Path = "/api/graphql",
    //      [property: JsonPropertyName("allow-introspection")]
    //      bool AllowIntrospection = true)
    //     : ApiSettings(Enabled, Path);

    // /// <summary>
    // /// Global settings related to hosting.
    // /// </summary>
    // /// <param name="Mode">The mode in which runtime is to be run.</param>
    // /// <param name="Cors">Settings related to Cross Origin Resource Sharing.</param>
    // /// <param name="Authentication">Authentication configuration properties.</param>
    // public record HostGlobalSettings
    //     (HostModeType Mode = HostModeType.Production,
    //      Cors? Cors = null,
    //      AuthenticationConfig? Authentication = null)
    //     : GlobalSettings();

    // /// <summary>
    // /// Configuration related to Cross Origin Resource Sharing (CORS).
    // /// </summary>
    // /// <param name="Origins">List of allowed origins.</param>
    // /// <param name="AllowCredentials">
    // /// Whether to set Access-Control-Allow-Credentials CORS header.</param>
    // public record Cors(string[]? Origins,
    //     [property: JsonPropertyName("allow-credentials")]
    //     bool AllowCredentials = true);

    // /// <summary>
    // /// Different global settings types.
    // /// </summary>
    // public enum GlobalSettingsType
    // {
    //     Rest,
    //     GraphQL,
    //     Host
    // }

    // /// <summary>
    // /// Different modes in which the runtime can run.
    // /// </summary>
    // public enum HostModeType
    // {
    //     Development,
    //     Production
    // }

    // public record DataSource(
    //     [property: JsonPropertyName("database-type")]
    //     DatabaseType DatabaseType,
    //     [property: JsonPropertyName("connection-string")]
    //     string ConnectionString);

    // /// <summary>
    // /// Options for CosmosDb database.
    // /// </summary>
    // public record CosmosDbOptions(string Database);

    // /// <summary>
    // /// Options for MsSql database.
    // /// </summary>
    // public record MsSqlOptions(
    //     [property: JsonPropertyName("set-session-context")]
    //     bool SetSessionContext = true);

    // /// <summary>
    // /// Options for PostgresSql database.
    // /// </summary>
    // public record PostgreSqlOptions;

    // /// <summary>
    // /// Options for MySql database.
    // /// </summary>
    // public record MySqlOptions;

    // /// <summary>
    // /// Enum for the supported database types.
    // /// </summary>
    // public enum DatabaseType
    // {
    //     cosmos,
    //     mssql,
    //     mysql,
    //     postgresql
    // }

    // public record RuntimeConfig(
    //     [property: JsonPropertyName("$schema")] string Schema,
    //     [property: JsonPropertyName("data-source")] DataSource DataSource,
    //     CosmosDbOptions? CosmosDb,
    //     MsSqlOptions? MsSql,
    //     PostgreSqlOptions? PostgreSql,
    //     MySqlOptions? MySql,
    //     [property: JsonPropertyName("runtime")]
    //     Dictionary<GlobalSettingsType, GlobalSettings> RuntimeSettings,
    //     Dictionary<string, Entity> Entities)
    // {
    //     public void SetDefaults()
    //     {
    //         foreach (
    //             (GlobalSettingsType settingsType, GlobalSettings settings) in RuntimeSettings)
    //         {
    //             switch (settingsType)
    //             {
    //                 case GlobalSettingsType.Rest:
    //                     if (settings is not RestGlobalSettings)
    //                     {
    //                         RuntimeSettings[settingsType] = new RestGlobalSettings();
    //                     }

    //                     break;
    //                 case GlobalSettingsType.GraphQL:
    //                     if (settings is not GraphQLGlobalSettings)
    //                     {
    //                         RuntimeSettings[settingsType] = new GraphQLGlobalSettings();
    //                     }

    //                     break;
    //                 case GlobalSettingsType.Host:
    //                     if (settings is not HostGlobalSettings)
    //                     {
    //                         RuntimeSettings[settingsType] = new HostGlobalSettings();
    //                     }

    //                     break;
    //                 default:
    //                     throw new NotSupportedException("The runtime does not " +
    //                         " support this global settings type.");
    //             }
    //         }
    //     }
    // }

    public class Config
    {
        // public DataSource data_source = new DataSource();
    }
}