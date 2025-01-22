// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel.GraphQL;

public class GraphQLSchemaMode
{
    [JsonPropertyName("data")]
    public required Data Data { get; set; }
}

public class Data
{
    [JsonPropertyName("__schema")]
    public required Schema Schema { get; set; }
}

public class Schema
{
    [JsonPropertyName("types")]
    public required Types[] Types { get; set; }
}

public class Types
{
    [JsonPropertyName("kind")]
    public required string Kind { get; set; }
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    [JsonPropertyName("fields")]
    public required Field[] Fields { get; set; }
}

public class Field
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    [JsonPropertyName("type")]
    public required Type Type { get; set; }
}

public class Type
{
    [JsonPropertyName("kind")]
    public required string Kind { get; set; }
    [JsonPropertyName("ofType")]
    public Type? OfType { get; set; }
}

/*
{
	"data": {
		"__schema": {
			"types": [
				{
					"kind": "OBJECT",
					"name": "Publisher",
					"fields": [
						{
							"name": "id"
						},
						{
							"name": "name"
						}
					]
				}
			]
		}
	}
}
*/
