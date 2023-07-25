// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Holds the global settings used at runtime for REST APIs.
/// </summary>
/// <param name="Enabled">If the REST APIs are enabled.</param>
/// <param name="Path">The URL prefix path at which endpoints
/// for all entities will be exposed.</param>
public record RestRuntimeOptions(bool Enabled = true, string Path = RestRuntimeOptions.DEFAULT_PATH, bool RequestBodyStrict = true)
{
    public const string DEFAULT_PATH = "/api";
};
