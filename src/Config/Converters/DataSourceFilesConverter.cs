// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Converter for DataSourceFiles
/// </summary>
class DataSourceFilesConverter : JsonConverter<DataSourceFiles>
{
    /// <inheritdoc/>
    public override DataSourceFiles? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        IEnumerable<string>? dataSourceFiles =
            JsonSerializer.Deserialize<IEnumerable<string>>(ref reader, options);

        return new DataSourceFiles(dataSourceFiles);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DataSourceFiles value, JsonSerializerOptions options)
    {
        // Remove the converter so we don't recurse.
        JsonSerializerOptions innerOptions = new(options);
        innerOptions.Converters.Remove(innerOptions.Converters.First(c => c is DataSourceFilesConverter));

        JsonSerializer.Serialize(writer, value.SourceFiles, innerOptions);
    }
}
