// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;

namespace Azure.DataApiBuilder.Config.ObjectModel;

[JsonConverter(typeof(EntityGraphQLOptionsConverter))]
public record EntityGraphQLOptions(string Singular, string Plural, bool Enabled = true, GraphQLOperation? Operation = null);
