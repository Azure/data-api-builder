namespace Azure.DataApiBuilder.Service.AuthenticationHelpers
{
    /// <summary>
    /// Default values related to StaticWebAppAuthentication handler.
    /// </summary>
    public static class EasyAuthAuthenticationDefaults
    {
        /// <summary>
        /// The default value used for StaticWebAppAuthenticationOptions.AuthenticationScheme.
        /// </summary>
        public const string AUTHENTICATIONSCHEME = "EasyAuthAuthentication";

        public const string INVALID_PAYLOAD_ERROR = "Invalid EasyAuth Payload.";
    }
}
