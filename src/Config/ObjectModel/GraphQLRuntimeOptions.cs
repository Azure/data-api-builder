// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record GraphQLRuntimeOptions(bool? Enabled = null, string? Path = null, bool? AllowIntrospection = null)
{
    public const string DEFAULT_PATH = "/graphql";
}
