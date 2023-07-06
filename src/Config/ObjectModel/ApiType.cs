// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Different types of APIs supported by runtime engine.
/// </summary>
public enum ApiType
{
    REST,
    GraphQL,
    // This is required to indicate features common between all APIs.
    All
}
