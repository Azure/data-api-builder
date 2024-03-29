// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record GraphQLRuntimeOptions(bool Enabled = true,
                                    string Path = GraphQLRuntimeOptions.DEFAULT_PATH,
                                    bool AllowIntrospection = true,
                                    MultipleMutationOptions? MultipleMutationOptions = null)
{
    public const string DEFAULT_PATH = "/graphql";
}
