// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class RuntimeConfigConditionalConverter : JsonConverter<RuntimeConfig>
{
    private readonly List<string> _propertiesToExclude;

    /// <summary>
    /// Constructor for converter.
    /// </summary>
    /// <param name="propertiesToExclude">properties to exclude.</param>
    public RuntimeConfigConditionalConverter(List<string> propertiesToExclude)
    {
        this._propertiesToExclude = propertiesToExclude;
    }

    public override RuntimeConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Use default implementation for deserialization
        return JsonSerializer.Deserialize<RuntimeConfig>(ref reader, options);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RuntimeConfig value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        PropertyInfo[] properties = typeof(RuntimeConfig).GetProperties();

        foreach (PropertyInfo property in properties)
        {
            if (!_propertiesToExclude.Contains(property.Name))
            {
                writer.WritePropertyName(property.Name);
                JsonSerializer.Serialize(writer, property.GetValue(value), options);
            }
        }

        writer.WriteEndObject();
    }
}
