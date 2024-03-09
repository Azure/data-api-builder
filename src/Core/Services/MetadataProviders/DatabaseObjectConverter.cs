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
        using (JsonDocument document = JsonDocument.ParseValue(ref reader))
        {
            JsonElement root = document.RootElement;
            string typeName = root.GetProperty("TypeName").GetString() ?? throw new JsonException("TypeName is missing");
            Type concreteType = GetTypeFromName(typeName);

            if (concreteType == null)
            {
                throw new JsonException($"Unknown type: {typeName}");
            }

            DatabaseObject objA = (DatabaseObject)JsonSerializer.Deserialize(document, concreteType, options)!;

            return objA;
        }
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
