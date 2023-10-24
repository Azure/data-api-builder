// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Service.Tests.Configuration;

internal static class ConfigurationEndpoints
{
    // TODO: Remove the old endpoint once we've updated all callers to use the new one.
    public const string CONFIGURATION_ENDPOINT = "/configuration";
    public const string CONFIGURATION_ENDPOINT_V2 = "/configuration/v2";
}
