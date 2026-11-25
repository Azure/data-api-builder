// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Data.SqlClient;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers;

/// <summary>
/// Configures SqlClient authentication with the application identity used for OBO.
/// </summary>
internal static class SqlClientAuthentication
{
    /// <summary>
    /// Registers a shared provider for every authentication method it supports.
    /// Leaves SqlClient's existing providers unchanged when no application client ID is configured.
    /// </summary>
    internal static void RegisterProviders()
    {
        string? clientId = Environment.GetEnvironmentVariable(UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        ActiveDirectoryAuthenticationProvider provider = new(clientId);
        foreach (SqlAuthenticationMethod method in Enum.GetValues<SqlAuthenticationMethod>())
        {
            if (provider.IsSupported(method) && !SqlAuthenticationProvider.SetProvider(method, provider))
            {
                throw new InvalidOperationException($"Failed to register the SqlClient authentication provider for {method}.");
            }
        }
    }
}
