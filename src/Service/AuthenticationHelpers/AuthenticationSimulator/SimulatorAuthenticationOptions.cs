using Azure.DataApiBuilder.Config;
using Microsoft.AspNetCore.Authentication;

namespace Azure.DataApiBuilder.Service.AuthenticationHelpers.AuthenticationSimulator
{
    /// <summary>
    /// A stub class to expose named options for the authentication simulator.
    /// Options are a required parameter of AuthenticationBuilder.AddScheme()
    /// which is utilized in the EasyAuthAuthenticationBuilderExtensions class.
    /// Microsoft Docs: https://docs.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.authenticationbuilder.addscheme?view=aspnetcore-6.0
    /// Follows the model demonstrated by Microsoft.Identity.Web
    /// https://github.com/AzureAD/microsoft-identity-web/blob/master/src/Microsoft.Identity.Web/AppServicesAuth/AppServicesAuthenticationOptions.cs
    /// </summary>
    public class SimulatorAuthenticationOptions : AuthenticationSchemeOptions
    {
        public SimulatorType SimulatorProvider { get; set; }
    }
}
