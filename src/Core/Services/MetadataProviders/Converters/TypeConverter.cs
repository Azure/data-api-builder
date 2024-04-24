// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders.Converters
{
    /// <summary>
    /// This is a converter to serialize and deserialize an object of Type : System.Type
    /// For example, the ColumnDefiniton object's default value property of type System.Type.
    /// </summary>
    public class TypeConverter : JsonConverter<Type>
    {
        public override Type Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException();
            }

            string typeName = reader.GetString()!;
            return Type.GetType(typeName)!;
        }

        public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
        {
            // Full Name is a shorter version of the assembly qualified name, full name works for serialization and 
            // deserialization of .Net types
            writer.WriteStringValue(value.FullName);
        }
    }
}
