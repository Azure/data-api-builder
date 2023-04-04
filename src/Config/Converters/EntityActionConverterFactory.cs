// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Azure.DataApiBuilder.Config.Converters;

internal class EntityActionConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(EntityAction));
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return new EntityActionConverter();
    }

    private class EntityActionConverter : JsonConverter<EntityAction>
    {
        public override EntityAction? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? actionOperation = reader.GetString();

                return new EntityAction(Enum.Parse<EntityActionOperation>(actionOperation!, true), new EntityActionFields(), new EntityActionPolicy(""));
            }

            JsonSerializerOptions innerOptions = new(options);
            innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is EntityActionConverterFactory));

            EntityAction? action = JsonSerializer.Deserialize<EntityAction>(ref reader, innerOptions);

            if (action is null)
            {
                return null;
            }

            return action with { Policy = action.Policy with { Database = ProcessFieldsInPolicy(action.Policy.Database) } };
        }

        public override void Write(Utf8JsonWriter writer, EntityAction value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Helper method which takes in the database policy and returns the processed policy
        /// without @item. directives before field names.
        /// </summary>
        /// <param name="policy">Raw database policy</param>
        /// <returns>Processed policy without @item. directives before field names.</returns>
        private static string ProcessFieldsInPolicy(string policy)
        {
            string fieldCharsRgx = @"@item\.([a-zA-Z0-9_]*)";

            // processedPolicy would be devoid of @item. directives.
            string processedPolicy = Regex.Replace(policy, fieldCharsRgx, (columnNameMatch) =>
                columnNameMatch.Groups[1].Value
            );
            return processedPolicy;
        }
    }
}
