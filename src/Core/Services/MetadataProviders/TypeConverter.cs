// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders.Converters
{
    /// <summary>
    /// This is a converter to serialize and deserialize the a object of Type : System.Type
    /// for example currently in ColumnDefiniton object, one of the property type is of System.Type property
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
            writer.WriteStringValue(value.AssemblyQualifiedName);
        }
    }
}
