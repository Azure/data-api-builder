using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Azure.DataGateway.Config
{
    /// <summary>
    /// Defines the Entities that are exposed.
    /// </summary>   
    /// <param name="Source">The underlying database object to which
    /// the exposed entity is connected to.</param>
    /// <param name="Rest">Can be a bool or RestEntitySettings type.
    /// When boolean, it describes if the entity is to be exposed.
    /// When RestEntitySettings, describes the REST endpoint settings
    /// specific to this entity.</param>
    /// <param name="GraphQL">Can be a bool or GraphQLEntitySettings type.
    /// When GraphQLEntitySettings, describes the GraphQL settings
    /// specific to this entity.</param>
    /// <param name="Permissions">Permissions assigned to this entity.</param>
    /// <param name="Relationships">Defines how an entity is related to other exposed
    /// entities and optionally provides details on what underlying database
    /// objects can be used to support such relationships.</param>
    /// <param name="Mappings"> Defines mappings between database fields
    /// and GraphQL and REST fields.</param>
    public record Entity(
        [property: JsonPropertyName("source")]
        object Source,
        [property: JsonPropertyName("rest")]
        object? Rest,
        [property: JsonPropertyName("graphql")]
        object? GraphQL,
        [property: JsonPropertyName("permissions")]
        PermissionSetting[] Permissions,
        [property: JsonPropertyName("relationships")]
        Dictionary<string, Relationship>? Relationships,
        [property: JsonPropertyName("mappings")]
        Dictionary<string, string>? Mappings)
    {
        public const string JSON_PROPERTY_NAME = "entities";

        /// <summary>
        /// Gets the name of the underlying source database object.
        /// </summary>
        public string GetSourceName()
        {
            if (Source is null)
            {
                return string.Empty;
            }

            if (((JsonElement)Source).ValueKind is JsonValueKind.String)
            {
                return JsonSerializer.Deserialize<string>((JsonElement)Source)!;
            }
            else
            {
                DatabaseObjectSource objectSource
                    = JsonSerializer.Deserialize<DatabaseObjectSource>((JsonElement)Source)!;
                return objectSource.Name;
            }
        }

        /// <summary>
        /// Helper method to return an object describing the database object source, mainly useful for getting stored procedure config data
        /// If only a string was passed in config, assume a table is returned and return no parameters
        /// If neither a string or object in config, fail initialization
        /// </summary>
        public DatabaseObjectSource GetSourceObject()
        {
            if (Source is null)
            {
                throw new JsonException(message: "Must specify entity source.");
            }

            JsonElement sourceJson = (JsonElement)Source;
            JsonSerializerSettings options = new()
            {
                Converters =
                {
                    new StringEnumConverter(new KebabCaseNamingStrategy())
                }
            };

            if (sourceJson.ValueKind is JsonValueKind.String)
            {
                return new(SourceType.Table, JsonSerializer.Deserialize<string>(sourceJson)!, null);
            }
            else if (sourceJson.ValueKind is JsonValueKind.Object)
            {
                // unfortunately, hyphenated string to enum conversion is impossible with system.text.json alone
                DatabaseObjectSource? objectSource = JsonConvert.DeserializeObject<DatabaseObjectSource>(Source.ToString()!, options);
                if (objectSource is null)
                {
                    throw new JsonException(message: "Could not deserialize source object.");
                }

                return objectSource;
            }

            throw new JsonException(message: $"Source not one of string or object");
        }
    }

    /// <summary>
    /// Describes the type, name and parameters for a
    /// database object source. Useful for more complex sources like stored procedures.
    /// </summary>
    /// <param name="Type">Type of the database object.</param>
    /// <param name="Name">The name of the database object.</param>
    /// <param name="Parameters">If Type is SourceType.StoredProcedure, Parameters to be
    /// passed as defaults to the procedure call. Allowed parameter value types are
    /// boolean/string/number/null </param>
    public record DatabaseObjectSource(
        SourceType Type,
        [property: JsonPropertyName("object")][JsonProperty("object")] string Name,
        Dictionary<string, JValue>? Parameters);

    /// <summary>
    /// Supported source types as defined by json schema
    /// </summary>
    public enum SourceType
    {
        Table,
        View,
        StoredProcedure
    }

    /// <summary>
    /// Describes the REST settings specific to an entity.
    /// </summary>
    /// <param name="Route">Instructs the runtime to use this route as the path
    /// at which the REST endpoint for this entity is exposed
    /// instead of using the entity-name. Can be a string or Singular-Plural type.
    /// If string, a corresponding plural route will be added as per the rules at
    /// <href="https://engdic.org/singular-and-plural-noun-rules-definitions-examples/" /></param>
    public record RestEntitySettings(object Route);

    /// <summary>
    /// Describes the GraphQL settings specific to an entity.
    /// </summary>
    /// <param name="Type">Defines the name of the GraphQL type
    /// that will be used for this entity.Can be a string or Singular-Plural type.
    /// If string, a default plural route will be added as per the rules at
    /// <href="https://engdic.org/singular-and-plural-noun-rules-definitions-examples/" /></param>
    public record GraphQLEntitySettings(object Type);

    /// <summary>
    /// Defines a name or route as singular (required) or
    /// plural (optional).
    /// </summary>
    /// <param name="Singular">Singular form of the name.</param>
    /// <param name="Plural">Optional pluralized form of the name.
    /// If plural is not specified, a default plural name will be used as per the rules at
    /// <href="https://engdic.org/singular-and-plural-noun-rules-definitions-examples/" /></param>
    public record SingularPlural(string Singular, string Plural);
}
