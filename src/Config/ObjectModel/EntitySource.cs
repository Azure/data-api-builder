// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record EntitySource(string Object, EntitySourceType Type, Dictionary<string, object>? Parameters, string[]? KeyFields);
