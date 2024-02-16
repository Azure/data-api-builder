// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Newtonsoft.Json;

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders
{
    /// <summary>
    /// Helper to generate GraphQL schema
    /// </summary>
    public class TypeConverter : JsonConverter
    {
        /// <summary>
        /// Helper to generate GraphQL schema
        /// </summary>
        /// <param name="objectType">objecttype</param>
        /// <returns>true or false</returns>
        public override bool CanConvert(Type objectType)
        {
            return typeof(Type).IsAssignableFrom(objectType);
        }

        /// <summary>
        /// Helper to generate GraphQL schema
        /// </summary>
        /// <param name="reader">reader</param>
        /// <param name="objectType">objecttype</param>
        /// <param name="existingValue">existing value</param>
        /// <param name="serializer">serialiser</param>
        /// <returns>true or false</returns>
        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                string typeName = (string)reader.Value!;
                return Type.GetType(typeName)!;
            }

            throw new JsonSerializationException("Unexpected token type when converting Type.");
        }

        /// <summary>
        /// Helper to generate GraphQL schema
        /// </summary>
        /// <param name="writer">reader</param>
        /// <param name="value">objecttype</param>
        /// <param name="serializer">serialiser</param>
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is Type type)
            {
                writer.WriteValue(type.AssemblyQualifiedName);
            }
            else
            {
                throw new JsonSerializationException("Expected Type object value.");
            }
        }
    }
}
