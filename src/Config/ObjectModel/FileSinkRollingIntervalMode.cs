// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the rolling interval options for file sink.
/// The time it takes between the creation of new files.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RollingIntervalMode
{
    Hour,
    Day,
    Week
}
