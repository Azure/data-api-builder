// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.DatabasePrimitives;

public class DatabaseObjectConverter : JsonConverter<DatabaseObject>
{
    public override DatabaseObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Check if the current token is the start of an object
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object.");
        }

        // Move to the next token
        if (!reader.Read())
        {
            throw new JsonException("Unexpected end of JSON while reading.");
        }

        // Read the TypeName property
        if (reader.TokenType != JsonTokenType.PropertyName || reader.GetString() != "TypeName")
        {
            throw new JsonException("Expected property 'TypeName'.");
        }

        // Move to the value of the TypeName property
        if (!reader.Read())
        {
            throw new JsonException("Unexpected end of JSON while reading.");
        }

        // Read the TypeName value
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string value for 'TypeName'.");
        }

        string typeName = reader.GetString() ?? throw new JsonException("TypeName is missing");
        Type concreteType = GetTypeFromName(typeName)!;

        if (concreteType == null)
        {
            throw new JsonException($"Unknown type: {typeName}");
        }

        // Move to the next token
        if (!reader.Read())
        {
            throw new JsonException("Unexpected end of JSON while reading.");
        }

        JsonDocument doc = JsonDocument.ParseValue(ref reader);

        // Deserialize the object using the concrete type
        DatabaseObject objA = (DatabaseObject)JsonSerializer.Deserialize(doc, concreteType, options)!;

        // Return the deserialized object
        return objA;
    }

    public override void Write(Utf8JsonWriter writer, DatabaseObject? value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // Add TypeName
        writer.WriteString("TypeName", value!.GetType().AssemblyQualifiedName);

        // Add other properties of DatabaseObject
        foreach (PropertyInfo prop in value.GetType().GetProperties())
        {
            // Skip the TypeName property, as it has been handled above
            if (prop.Name == "TypeName")
            {
                continue;
            }

            writer.WritePropertyName(prop.Name);
            JsonSerializer.Serialize(writer, prop.GetValue(value), options);
        }

        writer.WriteEndObject();
    }

    private static Type GetTypeFromName(string typeName)
    {
        Type type = Type.GetType(typeName)!;

        if (type == null)
        {
            throw new JsonException($"Could not find type: {typeName}");
        }

        return type;
    }
}
