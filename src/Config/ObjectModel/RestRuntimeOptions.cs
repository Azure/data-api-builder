// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Holds the global settings used at runtime for REST APIs.
/// </summary>
/// <param name="Enabled">If the REST APIs are enabled.</param>
/// <param name="Path">The URL prefix path at which endpoints
/// for all entities will be exposed.</param>
/// <param name="RequestBodyStrict">When true, extraneous/unmapped fields in the REST request body are rejected.
/// When false, extraneous fields are allowed and ignored.
/// The record default (true) preserves backward compatibility for existing configs that omit this property.
/// When dab init generates a new config, request-body-strict is set to false to allow extraneous fields by default.</param>
public record RestRuntimeOptions(bool Enabled = true, string Path = RestRuntimeOptions.DEFAULT_PATH, bool RequestBodyStrict = true)
{
    public const string DEFAULT_PATH = "/api";
};
