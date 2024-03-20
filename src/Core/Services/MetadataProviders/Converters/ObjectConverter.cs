// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders.Converters
{
    /// <summary>
    /// This is a converter to serialize and deserialize default
    /// can object can be of different types int, long, string, datetime.
    /// sample list of types :  SchemaConverter.cs  -> CreateValueNodeFromDbObjectNode
    /// https://github.com/Azure/data-api-builder/blob/main/src/Service.GraphQLBuilder/Sql/SchemaConverter.cs#L218
    /// This converter adds a property Type to the object during serialization which stores the type of object and stores the actual value in property Value
    /// which then gets used during deserialization.
    /// </summary>
    public class ObjectConverter : JsonConverter<object>
    {
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                JsonElement obj = doc.RootElement;
                string typeName = obj.GetProperty("Type").GetString()!;
                Type type = Type.GetType(typeName)!;
                object value = JsonSerializer.Deserialize(obj.GetProperty("Value").GetRawText(), type, options)!;
                return value;
            }
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            Type type = value.GetType();
            
            // Full Name is a shorter version of the assembly qualified name, full name works for serialization and 
            // deserialization of .Net types
            string typeName = type.FullName!;

            writer.WriteStartObject();
            writer.WriteString("Type", typeName);
            writer.WritePropertyName("Value");
            JsonSerializer.Serialize(writer, value, type, options);
            writer.WriteEndObject();
        }
    }
}
