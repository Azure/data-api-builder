using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config
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
        public SourceType ObjectType { get; private set; } = SourceType.Table;

        [JsonIgnore]
        public string SourceName { get; private set; } = string.Empty;

        [JsonIgnore]
        public Dictionary<string, object>? Parameters { get; private set; }

        [JsonIgnore]
        public string[]? KeyFields { get; private set; }

        [property: JsonPropertyName("graphql")]
        public object? GraphQL { get; set; } = GraphQL;

        /// <summary>
        /// Gets the name of the underlying source database object.
        /// Prefer accessing SourceName itself if TryPopulateSourceFields has been called
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
        /// Processes per entity GraphQL Naming Settings
        /// Top Level: true | false
        /// Alternatives: string, SingularPlural object
        /// returns true on successfull processing
        /// else false.
        /// </summary>
        public bool TryProcessGraphQLNamingConfig()
        {
            if (GraphQL is null)
            {
                return true;
            }

            if (GraphQL is JsonElement configElement)
            {
                if (configElement.ValueKind is JsonValueKind.True || configElement.ValueKind is JsonValueKind.False)
                {
                    GraphQL = JsonSerializer.Deserialize<bool>(configElement)!;
                }
                else if (configElement.ValueKind is JsonValueKind.Object)
                {
                    JsonElement nameTypeSettings = configElement.GetProperty("type");
                    object nameConfiguration;

                    if (nameTypeSettings.ValueKind is JsonValueKind.String)
                    {
                        nameConfiguration = JsonSerializer.Deserialize<string>(nameTypeSettings)!;
                    }
                    else if (nameTypeSettings.ValueKind is JsonValueKind.Object)
                    {
                        nameConfiguration = JsonSerializer.Deserialize<SingularPlural>(nameTypeSettings)!;
                    }
                    else
                    {
                        // Not Supported Type
                        return false;
                    }

                    GraphQLEntitySettings graphQLEntitySettings = new(Type: nameConfiguration);
                    GraphQL = graphQLEntitySettings;
                }
            }
            else
            {
                // Not Supported Type
                return false;
            }

            return true;
        }

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

            JsonElement sourceJson = JsonSerializer.SerializeToElement(Source);

            // In the case of a simple, string source, we assume the source type is a table; parameters and key fields left null
            // Note: engine supports views backing entities labeled as Tables, as long as their primary key can be inferred
            if (sourceJson.ValueKind is JsonValueKind.String)
            {
                ObjectType = SourceType.Table;
                SourceName = JsonSerializer.Deserialize<string>(sourceJson)!;
                Parameters = null;
                KeyFields = null;
            }
            else if (sourceJson.ValueKind is JsonValueKind.Object)
            {
                DatabaseObjectSource? objectSource
                    = JsonSerializer.Deserialize<DatabaseObjectSource>(sourceJson,
                    options: RuntimeConfig.SerializerOptions);

                if (objectSource is null)
                {
                    throw new JsonException(message: "Could not deserialize source object.");
                }
                else
                {
                    ObjectType = objectSource.Type;
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
    }

    /// <summary>
    /// Describes the type, name, parameters, and key fields for a
    /// database object source.
    /// </summary>
    /// <param name="Type"> Type of the database object.
    /// Should be one of [table, view, stored-procedure, function]. </param>
    /// <param name="Name"> The name of the database object. </param>
    /// <param name="Parameters"> If Type is SourceType.(StoredProcedure|Function),
    /// Parameters to be passed as defaults to the procedure/function call </param>
    /// <param name="KeyFields"> The field(s) to be used as primary keys.
    public record DatabaseObjectSource(
        [property: JsonConverter(typeof(SourceTypeEnumConverter))]
        SourceType Type,
        [property: JsonPropertyName("object")]
            string Name,
        Dictionary<string, object>? Parameters,
        [property: JsonPropertyName("key-fields")]
            string[]? KeyFields);

    /// <summary>
    /// Class to specify custom converter used while deserialising json config
    /// to SourceType and serializing from SourceType to string.
    /// Tries to convert the given string sourceType into one of the supported SourceType enums
    /// Throws an exception if not a case-insensitive match
    /// </summary>
    public class SourceTypeEnumConverter : JsonConverter<SourceType>
    {
        public const string STORED_PROCEDURE = "stored-procedure";
        public static readonly string[] VALID_SOURCE_TYPE_VALUES = {
            STORED_PROCEDURE,
            SourceType.Table.ToString().ToLower(),
            SourceType.View.ToString().ToLower()
        };

        /// <inheritdoc/>
        public override SourceType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? type = reader.GetString();
            if (!TryGetSourceType(type, out SourceType objectType))
            {
                throw new JsonException(GenerateMessageForInvalidSourceType(type!));
            }

            return objectType;
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, SourceType value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.GetConfigValue());
        }

        /// <summary>
        /// For the provided type as an string argument,
        /// try to get the underlying Enum SourceType, if it exists,
        /// and saves in out objectType, and return true, otherwise return false.
        /// </summary>
        public static bool TryGetSourceType(string? type, out SourceType objectType)
        {
            if (type is null)
            {
                objectType = SourceType.Table;  // Assume Default type as Table if type not provided.
            }
            else if (STORED_PROCEDURE.Equals(type, StringComparison.OrdinalIgnoreCase))
            {
                objectType = SourceType.StoredProcedure;
            }
            else if (!Enum.TryParse<SourceType>(type, ignoreCase: true, out objectType))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns the source type of the given source object.
        /// </summary>
        public static SourceType GetSourceTypeFromSource(object source)
        {
            if (typeof(string).Equals(source.GetType()))
            {
                return SourceType.Table;
            }

            return ((DatabaseObjectSource)source).Type;
        }

        /// <summary>
        /// Generates an error message for invalid source type.
        /// Message also includes the acceptable values of source type.
        /// </summary>
        public static string GenerateMessageForInvalidSourceType(string invalidType)
        {
            return $"Invalid Source Type: {invalidType}." +
                    $" Valid values are: {string.Join(",", VALID_SOURCE_TYPE_VALUES)}";
        }
    }

    /// <summary>
    /// Supported source types as defined by json schema
    /// </summary>
    public enum SourceType
    {
        [Description("table")]
        Table,
        [Description("view")]
        View,
        [Description("stored-procedure")]
        StoredProcedure,
        [Description("function")]
        Function
    }

    /// <summary>
    /// Defines an extension method of SourceType enum to get the description of the
    /// enum value
    /// </summary>
    public static class SourceTypeHelper
    {
        /// <summary>
        /// Gets the DAB config value for the source type
        /// </summary>
        public static string GetConfigValue(this SourceType sourceType)
        {
            return sourceType is SourceType.StoredProcedure ?
                    SourceTypeEnumConverter.STORED_PROCEDURE :
                    sourceType.ToString().ToLower();
        }

        /// <summary>
        /// Check if the source type is stored-procedure or a function
        /// </summary>
        public static bool IsDatabaseExecutableType(this SourceType sourceType)
        {
            return sourceType is SourceType.StoredProcedure || sourceType is SourceType.Function;
        }
    }

    /// <summary>
    /// Describes the REST settings specific to an entity.
    /// </summary>
    /// <param name="Path">Instructs the runtime to use this as the path
    /// at which the REST endpoint for this entity is exposed
    /// instead of using the entity-name. Can be a string type.
    /// </param>
    public record RestEntitySettings(object Path);

    /// <summary>
    /// Describes the GraphQL settings specific to an entity.
    /// </summary>
    /// <param name="Type">Defines the name of the GraphQL type
    /// that will be used for this entity.Can be a string or Singular-Plural type.
    /// If string, a default plural route will be added as per the rules at
    /// <href="https://engdic.org/singular-and-plural-noun-rules-definitions-examples/" /></param>
    public record GraphQLEntitySettings([property: JsonPropertyName("type")] object? Type);

    /// <summary>
    /// Defines a name or route as singular (required) or
    /// plural (optional).
    /// </summary>
    /// <param name="Singular">Singular form of the name.</param>
    /// <param name="Plural">Optional pluralized form of the name.
    /// If plural is not specified, a default plural name will be used as per the rules at
    /// <href="https://engdic.org/singular-and-plural-noun-rules-definitions-examples/" /></param>
    public record SingularPlural(
            [property: JsonPropertyName("singular")] string Singular,
            [property: JsonPropertyName("plural")] string? Plural);
}
