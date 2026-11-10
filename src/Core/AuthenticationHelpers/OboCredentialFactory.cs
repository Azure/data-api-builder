// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.Core;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace Azure.DataApiBuilder.Core.AuthenticationHelpers
{
    /// <summary>
    /// Validates and creates the application credential used for user-delegated authentication.
    /// </summary>
    public static class OboCredentialFactory
    {
        public const string MISSING_CREDENTIALS_ERROR_MESSAGE =
            "User-delegated authentication requires DAB_OBO_CLIENT_ID, DAB_OBO_TENANT_ID, and exactly one of DAB_OBO_CLIENT_SECRET or DAB_OBO_MANAGED_IDENTITY_CLIENT_ID.";

        private const string CONFLICTING_CREDENTIALS_ERROR_MESSAGE =
            "Configure exactly one OBO credential method. DAB_OBO_CLIENT_SECRET and DAB_OBO_MANAGED_IDENTITY_CLIENT_ID cannot both be set.";

        private const string TOKEN_EXCHANGE_SCOPE = "api://AzureADTokenExchange/.default";

        /// <summary>
        /// Validates the environment variables for the selected OBO application credential.
        /// Callers are responsible for checking whether user-delegated authentication is enabled.
        /// </summary>
        public static void ValidateEnvironmentVariables()
        {
            ValidateEnvironmentVariables(Environment.GetEnvironmentVariable);
        }

        /// <summary>
        /// Creates a confidential client whose credential is independent of the incoming user assertion.
        /// The returned client should be reused for subsequent OBO requests.
        /// </summary>
        public static IConfidentialClientApplication CreateClient(ILogger logger)
        {
            return CreateClient(logger, Environment.GetEnvironmentVariable);
        }

        internal static void ValidateEnvironmentVariables(Func<string, string?> environmentVariableReader)
        {
            _ = ReadOptions(environmentVariableReader);
        }

        internal static IConfidentialClientApplication CreateClient(
            ILogger logger,
            Func<string, string?> environmentVariableReader,
            Func<string, TokenCredential>? managedIdentityCredentialFactory = null,
            IMsalHttpClientFactory? httpClientFactory = null)
        {
            ArgumentNullException.ThrowIfNull(logger);
            OboCredentialOptions options = ReadOptions(environmentVariableReader);

            ConfidentialClientApplicationBuilder builder = ConfidentialClientApplicationBuilder
                .Create(options.ClientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, options.TenantId);

            if (options.ClientSecret is not null)
            {
                builder.WithClientSecret(options.ClientSecret);
            }
            else
            {
                TokenCredential credential = managedIdentityCredentialFactory is null
                    ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId!))
                    : managedIdentityCredentialFactory(options.ManagedIdentityClientId!);

                // Reuse the credential, not a token snapshot: Azure Identity handles assertion refresh.
                builder.WithClientAssertion((AssertionRequestOptions requestOptions) =>
                    GetManagedIdentityAssertionAsync(credential, logger, requestOptions.CancellationToken));
            }

            if (httpClientFactory is not null)
            {
                builder.WithHttpClientFactory(httpClientFactory);
            }

            return builder.Build();
        }

        private static async Task<string> GetManagedIdentityAssertionAsync(
            TokenCredential credential,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccessToken assertion = await credential.GetTokenAsync(
                    new TokenRequestContext([TOKEN_EXCHANGE_SCOPE]),
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(assertion.Token) || assertion.ExpiresOn <= DateTimeOffset.UtcNow)
                {
                    throw CreateAuthenticationFailure(logger);
                }

                return assertion.Token;
            }
            catch (AuthenticationFailedException)
            {
                // CredentialUnavailableException derives from AuthenticationFailedException.
                cancellationToken.ThrowIfCancellationRequested();
                throw CreateAuthenticationFailure(logger);
            }
        }

        private static DataApiBuilderException CreateAuthenticationFailure(ILogger logger)
        {
            // SDK exceptions can contain credential responses. Do not log or retain their payloads.
            logger.LogError(
                "{EventType}: Unable to acquire the managed identity client assertion for user-delegated authentication.",
                "OboClientAssertionAcquisitionFailed");

            return new DataApiBuilderException(
                message: DataApiBuilderException.OBO_TOKEN_ACQUISITION_FAILED,
                statusCode: HttpStatusCode.Unauthorized,
                subStatusCode: DataApiBuilderException.SubStatusCodes.OboAuthenticationFailure);
        }

        private static OboCredentialOptions ReadOptions(Func<string, string?> environmentVariableReader)
        {
            ArgumentNullException.ThrowIfNull(environmentVariableReader);

            string? clientId = environmentVariableReader(UserDelegatedAuthOptions.DAB_OBO_CLIENT_ID_ENV_VAR);
            string? tenantId = environmentVariableReader(UserDelegatedAuthOptions.DAB_OBO_TENANT_ID_ENV_VAR);
            string? clientSecret = environmentVariableReader(UserDelegatedAuthOptions.DAB_OBO_CLIENT_SECRET_ENV_VAR);
            string? managedIdentityClientId = environmentVariableReader(UserDelegatedAuthOptions.DAB_OBO_MANAGED_IDENTITY_CLIENT_ID_ENV_VAR);
            bool hasClientSecret = !string.IsNullOrWhiteSpace(clientSecret);
            bool hasManagedIdentity = !string.IsNullOrWhiteSpace(managedIdentityClientId);

            if (hasClientSecret && hasManagedIdentity)
            {
                throw CreateConfigurationFailure(CONFLICTING_CREDENTIALS_ERROR_MESSAGE);
            }

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(tenantId)
                || (!hasClientSecret && !hasManagedIdentity))
            {
                throw CreateConfigurationFailure(MISSING_CREDENTIALS_ERROR_MESSAGE);
            }

            return new OboCredentialOptions(
                clientId,
                tenantId,
                hasClientSecret ? clientSecret : null,
                hasManagedIdentity ? managedIdentityClientId : null);
        }

        private static DataApiBuilderException CreateConfigurationFailure(string message)
        {
            return new DataApiBuilderException(
                message: message,
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
        }

        private sealed class OboCredentialOptions
        {
            public string ClientId { get; }
            public string TenantId { get; }
            public string? ClientSecret { get; }
            public string? ManagedIdentityClientId { get; }

            public OboCredentialOptions(
                string clientId,
                string tenantId,
                string? clientSecret,
                string? managedIdentityClientId)
            {
                ClientId = clientId;
                TenantId = tenantId;
                ClientSecret = clientSecret;
                ManagedIdentityClientId = managedIdentityClientId;
            }
        }
    }
}
