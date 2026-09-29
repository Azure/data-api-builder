// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers;

/// <summary>
/// Authentication scheme name previously used for generic OAuth providers.
/// </summary>
/// <remarks>
/// Retained only for backward binary/source compatibility of the public
/// <c>Microsoft.DataApiBuilder.Core</c> package surface. DAB no longer uses this scheme:
/// custom OAuth/JWT providers are authenticated with the registered
/// <see cref="Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme"/>
/// ("Bearer") scheme. The "OAuthAuthentication" scheme was never registered as an authentication
/// handler, so referencing it results in a failed authentication.
/// </remarks>
[System.Obsolete("Unused and unsupported. Custom OAuth/JWT providers resolve to JwtBearerDefaults.AuthenticationScheme (\"Bearer\"). The \"OAuthAuthentication\" scheme is never registered.")]
public class GenericOAuthDefaults
{
    [System.Obsolete("Unused and unsupported. Custom OAuth/JWT providers resolve to JwtBearerDefaults.AuthenticationScheme (\"Bearer\"). The \"OAuthAuthentication\" scheme is never registered.")]
    public const string AUTHENTICATIONSCHEME = "OAuthAuthentication";
}
