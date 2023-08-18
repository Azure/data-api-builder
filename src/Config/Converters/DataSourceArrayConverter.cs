// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

class DataSourceArrayConverter : JsonConverter<IEnumerable<DataSource>>
{
    public override IEnumerable<DataSource> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                DataSource singleDataSource = JsonSerializer.Deserialize<DataSource>(doc.RootElement.GetRawText(), options)!;
                return new List<DataSource> { singleDataSource };
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<IEnumerable<DataSource>>(doc.RootElement.GetRawText(), options)!;
            }
            else
            {
                throw new JsonException("Unexpected data source format.");
            }
        }
    }

    public override void Write(Utf8JsonWriter writer, IEnumerable<DataSource> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
