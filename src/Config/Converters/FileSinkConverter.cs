// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;
using Serilog;

namespace Azure.DataApiBuilder.Config.Converters;
class FileSinkConverter : JsonConverter<FileSinkOptions>
{
    // Determines whether to replace environment variable with its
    // value or not while deserializing.
    private bool _replaceEnvVar;

    /// <param name="replaceEnvVar">
    /// Whether to replace environment variable with its value or not while deserializing.
    /// </param>
    public FileSinkConverter(bool replaceEnvVar)
    {
        _replaceEnvVar = replaceEnvVar;
    }

    /// <summary>
    /// Defines how DAB reads File Sink options and defines which values are
    /// used to instantiate FileSinkOptions.
    /// </summary>
    /// <exception cref="JsonException">Thrown when improperly formatted File Sink options are provided.</exception>
    public override FileSinkOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            bool? enabled = null;
            string? path = null;
            RollingInterval? rollingInterval = null;
            int? retainedFileCountLimit = null;
            long? fileSizeLimitBytes = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new FileSinkOptions(enabled, path, rollingInterval, retainedFileCountLimit, fileSizeLimitBytes);
                }

                string? propertyName = reader.GetString();

                reader.Read();
                switch (propertyName)
                {
                    case "enabled":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            enabled = reader.GetBoolean();
                        }

                        break;

                    case "path":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            path = reader.DeserializeString(_replaceEnvVar);
                        }

                        break;

                    case "rolling-interval":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            rollingInterval = EnumExtensions.Deserialize<RollingInterval>(reader.DeserializeString(_replaceEnvVar)!);
                        }

                        break;

                    case "retained-file-count-limit":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            try
                            {
                                retainedFileCountLimit = reader.GetInt32();
                            }
                            catch (FormatException)
                            {
                                throw new JsonException($"The JSON token value is of the incorrect numeric format.");
                            }

                            if (retainedFileCountLimit <= 0)
                            {
                                throw new JsonException($"Invalid retained-file-count-limit: {retainedFileCountLimit}. Specify a number > 0.");
                            }
                        }

                        break;

                    case "file-size-limit-bytes":
                        if (reader.TokenType is not JsonTokenType.Null)
                        {
                            try
                            {
                                fileSizeLimitBytes = reader.GetInt64();
                            }
                            catch (FormatException)
                            {
                                throw new JsonException($"The JSON token value is of the incorrect numeric format.");
                            }

                            if (retainedFileCountLimit <= 0)
                            {
                                throw new JsonException($"Invalid file-size-limit-bytes: {fileSizeLimitBytes}. Specify a number > 0.");
                            }
                        }

                        break;

                    default:
                        throw new JsonException($"Unexpected property {propertyName}");
                }
            }
        }

        throw new JsonException("Failed to read the File Sink Options");
    }

    /// <summary>
    /// When writing the FileSinkOptions back to a JSON file, only write the properties
    /// if they are user provided. This avoids polluting the written JSON file with properties
    /// the user most likely omitted when writing the original DAB runtime config file.
    /// This Write operation is only used when a RuntimeConfig object is serialized to JSON.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, FileSinkOptions value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (value?.UserProvidedEnabled is true)
        {
            writer.WritePropertyName("enabled");
            JsonSerializer.Serialize(writer, value.Enabled, options);
        }

        if (value?.UserProvidedPath is true)
        {
            writer.WritePropertyName("path");
            JsonSerializer.Serialize(writer, value.Path, options);
        }

        if (value?.UserProvidedRollingInterval is true)
        {
            writer.WritePropertyName("rolling-interval");
            JsonSerializer.Serialize(writer, value.RollingInterval, options);
        }

        if (value?.UserProvidedRetainedFileCountLimit is true)
        {
            writer.WritePropertyName("retained-file-count-limit");
            JsonSerializer.Serialize(writer, value.RetainedFileCountLimit, options);
        }

        if (value?.UserProvidedFileSizeLimitBytes is true)
        {
            writer.WritePropertyName("file-size-limit-bytes");
            JsonSerializer.Serialize(writer, value.FileSizeLimitBytes, options);
        }

        writer.WriteEndObject();
    }
}
