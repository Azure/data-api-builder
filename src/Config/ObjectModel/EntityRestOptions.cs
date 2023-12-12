// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Describes the REST settings specific to an entity.
/// </summary>
/// <param name="Path">Instructs the runtime to use this as the path
/// at which the REST endpoint for this entity is exposed
/// instead of using the entity-name. Can be a string type.
/// </param>
/// <param name="Methods">The HTTP verbs that are supported for this entity. Has significance only for stored-procedures.
/// For tables and views, all the 5 HTTP actions are enabled when REST endpoints are enabled 
/// for the entity. So, this property is insignificant for tables and views. </param>
/// <param name="Enabled">Whether the entity is enabled for REST.</param>
public record EntityRestOptions(SupportedHttpVerb[]? Methods = null, string? Path = null, bool Enabled = true)
{
    public static readonly SupportedHttpVerb[] DEFAULT_SUPPORTED_VERBS = new[] { SupportedHttpVerb.Get, SupportedHttpVerb.Post, SupportedHttpVerb.Put, SupportedHttpVerb.Patch, SupportedHttpVerb.Delete };
    public static readonly SupportedHttpVerb[] DEFAULT_HTTP_VERBS_ENABLED_FOR_SP = new[] { SupportedHttpVerb.Post };
}
