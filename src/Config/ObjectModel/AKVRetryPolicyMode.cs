// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AKVRetryPolicyMode
{
    // Fixed retry policy mode will use a fixed value when waiting on retries
    Fixed,
    // Exponential retry policy mode will use exponential back-off when waiting on retries
    Exponential
}
