// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeOptions
{
    public RestRuntimeOptions Rest;
    public GraphQLRuntimeOptions GraphQL;
    public HostOptions Host;
    public string? BaseRoute;

    [JsonConstructor]
    public RuntimeOptions(RestRuntimeOptions? Rest, GraphQLRuntimeOptions? GraphQL, HostOptions? Host, string? BaseRoute = null)
    {
        this.Rest = Rest ?? new RestRuntimeOptions();
        this.GraphQL = GraphQL ?? new GraphQLRuntimeOptions();
        this.Host = Host ?? HostOptions.GetDefaultHostOptions(
            HostMode.Development,
            corsOrigin: null,
            EasyAuthType.StaticWebApps.ToString(),
            audience: null, issuer: null);
    }
}
