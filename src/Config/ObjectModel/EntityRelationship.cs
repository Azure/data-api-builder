// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record EntityRelationship(
    Cardinality Cardinality,
    [property: JsonPropertyName("target.entity")] string TargetEntity,
    [property: JsonPropertyName("source.fields")] string[] SourceFields,
    [property: JsonPropertyName("target.fields")] string[] TargetFields,
    [property: JsonPropertyName("linking.object")] string? LinkingObject,
    [property: JsonPropertyName("linking.source.fields")] string[] LinkingSourceFields,
    [property: JsonPropertyName("linking.target.fields")] string[] LinkingTargetFields);
