using System.Text.Json;
using System.Text.Json.Serialization;

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

        [JsonIgnore]
        public SourceType ObjectType { get; private set; } = new();

        [JsonIgnore]
        public string SourceName { get; private set; } = String.Empty;

        [JsonIgnore]
        public Dictionary<string, object>? Parameters { get; private set; }

        [JsonIgnore]
        public Array? KeyFields { get; private set; }

        /// <summary>
        /// After the Entity has been deserialized, populate the source-related fields
        /// Deserialize into DatabaseObjectSource to parse fields if source is an object
        /// This allows us to avoid using Newtonsoft for direct deserialization
        /// Called at deserialization time - in RuntimeConfigProvider
        /// </summary>
        public void TryPopulateSourceFields()
        {
            if (Source is null)
            {
                throw new JsonException(message: "Must specify entity source.");
            }

            JsonElement sourceJson = (JsonElement)Source;

            // In the case of a simple, string source, we assume the source type is a table
            // Parameters and key fields are left null
            if (sourceJson.ValueKind is JsonValueKind.String)
            {
                ObjectType = SourceType.Table;
                SourceName = JsonSerializer.Deserialize<string>((JsonElement)Source)!;
            }
            else if (sourceJson.ValueKind is JsonValueKind.Object)
            {
                DatabaseObjectSource? objectSource
                    = JsonSerializer.Deserialize<DatabaseObjectSource>((JsonElement)Source,
                    options: RuntimeConfig.SerializerOptions);

                if (objectSource is null)
                {
                    throw new JsonException(message: "Could not deserialize source object.");
                }
                else
                {
                    ObjectType = ConvertSourceType(objectSource.Type);
                    SourceName = objectSource.Name;
                    Parameters = objectSource.Parameters;
                    KeyFields = objectSource.KeyFields;
                }

            }
            else
            {
                throw new JsonException(message: $"Source not one of string or object");
            }

        }

        /// <summary>
        /// Tries to convert the given string sourceType into one of the supported SourceType enums
        /// Throws an exception if not an exact, case-sensitive match
        /// </summary>
        private static SourceType ConvertSourceType(string? sourceType)
        {
            return sourceType switch
            {
                "table" => SourceType.Table,
                "view" => SourceType.View,
                "stored-procedure" => SourceType.StoredProcedure,
                _ => throw new JsonException(message: "Source type must be one of: [table, view, stored-procedure]")
            };
        }

        /// <summary>
        /// Describes the type, name, parameters, and key fields for a
        /// database object source.
        /// </summary>
        /// <param name="Type"> Type of the database object.
        /// Should be one of [table, view, stored-procedure]. </param>
        /// <param name="Name"> The name of the database object. </param>
        /// <param name="Parameters"> If Type is SourceType.StoredProcedure,
        /// Parameters to be passed as defaults to the procedure call </param>
        /// <param name="KeyFields"> The field(s) to be used as primary keys.
        /// Support tracked in #547 </param>
        public record DatabaseObjectSource(
            string Type,
            [property: JsonPropertyName("object")]
            string Name,
            Dictionary<string, object>? Parameters,
            [property: JsonPropertyName("key-fields")]
            Array KeyFields);

    }

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
