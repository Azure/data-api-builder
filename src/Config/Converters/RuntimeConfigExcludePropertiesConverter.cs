// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// This class is used to exclude properties from the RuntimeConfig when serializing.
/// </summary>
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

        // Remove the converter so we don't recurse.
        JsonSerializerOptions innerOptions = new(options);
        innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is RuntimeConfigConditionalConverter));

        // Use default implementation for deserialization
        return JsonSerializer.Deserialize<RuntimeConfig>(ref reader, innerOptions);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RuntimeConfig value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("$schema", value.Schema);

        PropertyInfo[] properties = typeof(RuntimeConfig).GetProperties();

        foreach (PropertyInfo property in properties)
        {
            if (!_propertiesToExclude.Contains(property.Name))
            {
                writer.WritePropertyName(RuntimeConfigLoader.GenerateHyphenatedName(property.Name));
                JsonSerializer.Serialize(writer, property.GetValue(value), options);
            }
        }

        writer.WriteEndObject();
    }
}
