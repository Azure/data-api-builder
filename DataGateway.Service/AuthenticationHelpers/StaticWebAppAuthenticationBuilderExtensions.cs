using Microsoft.AspNetCore.Authentication;

namespace Azure.DataGateway.Service.AuthenticationHelpers
{
    /// <summary>
    /// Extension methods related to Static Web App authentication (Easy Auth).
    /// This class allows setting up SWA AuthN in the startup class with
    /// a single call to .AddAuthentiction(scheme).AddStaticWebAppAuthentication()
    /// </summary>
    public static class StaticWebAppAuthenticationBuilderExtensions
    {
        /// <summary>
        /// Add authentication with Static Web Apps.
        /// </summary>
        /// <param name="builder">Authentication builder.</param>
        /// <returns>The builder, to chain commands.</returns>
        public static AuthenticationBuilder AddStaticWebAppAuthentication(
             this AuthenticationBuilder builder)
        {
            if (builder is null)
            {
                throw new System.ArgumentNullException(nameof(builder));
            }

            builder.AddScheme<StaticWebAppAuthenticationOptions, StaticWebAppAuthenticationHandler>(
                authenticationScheme: StaticWebAppAuthenticationDefaults.AUTHENTICATIONSCHEME,
                displayName: StaticWebAppAuthenticationDefaults.AUTHENTICATIONSCHEME,
                options => { });

            return builder;
        }
    }
}
