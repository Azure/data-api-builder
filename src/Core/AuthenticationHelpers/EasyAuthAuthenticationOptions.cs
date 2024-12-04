// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.Authentication;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers;

/// <summary>
/// A stub class to expose named options for Azure Static Web Apps/App Service authentication (Easy Auth).
/// Options are a required parameter of AuthenticationBuilder.AddScheme()
/// which is utilized in the EasyAuthAuthenticationBuilderExtensions class.
/// Microsoft Docs: https://docs.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.authenticationbuilder.addscheme
/// Follows the model demonstrated by Microsoft.Identity.Web
/// https://github.com/AzureAD/microsoft-identity-web/blob/master/src/Microsoft.Identity.Web/AppServicesAuth/AppServicesAuthenticationOptions.cs
/// </summary>
public class EasyAuthAuthenticationOptions : AuthenticationSchemeOptions
{
    public EasyAuthType EasyAuthProvider { get; set; }
}
