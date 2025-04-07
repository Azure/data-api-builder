// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers;

internal static class SupportedAuthNProviders
{
    public const string APP_SERVICE = "AppService";

    public const string AZURE_AD = "AzureAD";
    public const string ENTRA_ID = "EntraID";

    public const string GENERIC_OAUTH = "Custom";
    public const string SIMULATOR = "Simulator";

    public const string STATIC_WEB_APPS = "StaticWebApps";
}
