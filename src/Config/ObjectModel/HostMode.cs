// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HostMode
{
    Development,
    Production
}
