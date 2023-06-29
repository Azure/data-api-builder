// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// A subset of the HTTP verb list that is supported by the REST endpoints within the service.
/// </summary>
public enum SupportedHttpVerb
{
    Get,
    Post,
    Put,
    Patch,
    Delete
}
