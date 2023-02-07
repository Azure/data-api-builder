// **************************************
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// @file: EasyAuthAuthenticationDefaults.cs
// **************************************

namespace Azure.DataApiBuilder.Service.AuthenticationHelpers
{
    /// <summary>
    /// Default values related to EasyAuthAuthentication handler.
    /// </summary>
    public static class EasyAuthAuthenticationDefaults
    {
        /// <summary>
        /// The default value used for EasyAuthAuthenticationOptions.AuthenticationScheme.
        /// </summary>
        public const string AUTHENTICATIONSCHEME = "EasyAuthAuthentication";

        public const string INVALID_PAYLOAD_ERROR = "Invalid EasyAuth Payload.";
    }
}
