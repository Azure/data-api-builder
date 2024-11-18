// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// This is the list Authentication Providers possible in the Host Runtime Settings 
/// </summary>
public enum AuthProvider
{
    StaticWebApps,
    AppService,
    AzureAD,
    Jwt
}
