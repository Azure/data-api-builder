// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record GraphQLRuntimeOptions(bool Enabled = true,
                                    string Path = GraphQLRuntimeOptions.DEFAULT_PATH,
                                    bool AllowIntrospection = true,
                                    int? DepthLimit = null,
                                    MultipleMutationOptions? MultipleMutationOptions = null,
                                    bool EnableAggregation = true)
{
    public const string DEFAULT_PATH = "/graphql";

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write depth-limit
    /// property and value to the runtime config file.
    /// When user doesn't provide the depth-limit property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(DepthLimit))]
    public bool UserProvidedDepthLimit { get; init; } = false;
}
