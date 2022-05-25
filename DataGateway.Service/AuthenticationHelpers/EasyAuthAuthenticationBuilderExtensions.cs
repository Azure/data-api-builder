using Microsoft.AspNetCore.Authentication;

namespace Azure.DataGateway.Service.AuthenticationHelpers
{
    /// <summary>
    /// Extension methods related to Static Web App/ App Service authentication (Easy Auth).
    /// This class allows setting up Easy Auth authentication in the startup class with
    /// a single call to .AddAuthentiction(scheme).AddStaticWebAppAuthentication()
    /// </summary>
    public static class EasyAuthAuthenticationBuilderExtensions
    {
        /// <summary>
        /// Add authentication with Static Web Apps.
        /// </summary>
        /// <param name="builder">Authentication builder.</param>
        /// <returns>The builder, to chain commands.</returns>
        public static AuthenticationBuilder AddEasyAuthAuthentication(
             this AuthenticationBuilder builder)
        {
            if (builder is null)
            {
                throw new System.ArgumentNullException(nameof(builder));
            }

            builder.AddScheme<EasyAuthAuthenticationOptions, EasyAuthAuthenticationHandler>(
                authenticationScheme: EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME,
                displayName: EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME,
                options => { });

            return builder;
        }
    }
}
