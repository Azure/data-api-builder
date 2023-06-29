// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeOptions(RestRuntimeOptions Rest, GraphQLRuntimeOptions GraphQL, HostOptions Host)
{
    public const string PROPERTY_NAME_PATH = "path";
    public const string JSON_PROPERTY_NAME = "runtime";
}
