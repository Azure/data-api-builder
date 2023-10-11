// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Holds the global settings used at runtime for REST APIs.
/// </summary>
/// <param name="Enabled">If the REST APIs are enabled.</param>
/// <param name="Path">The URL prefix path at which endpoints
/// for all entities will be exposed.</param>
/// <param name="RequestBodyStrict">Boolean property indicating whether extraneous fields are allowed in request body.
/// The default behavior is true - meaning we don't allow extraneous fields by default in the rest request body.
/// Null value implies the default behavior will take effect.
/// Changing the default value is a breaking change.</param>
public record RestRuntimeOptions(bool? Enabled = null, string? Path = null, bool? RequestBodyStrict = null)
{
    public const string DEFAULT_PATH = "/api";
};
