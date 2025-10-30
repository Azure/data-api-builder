// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Config
{
    public class DeserializationVariableReplacementSettings
    {
        public bool DoReplaceEnvVar { get; set; } = true;
        public bool DoReplaceAkvVar { get; set; } = true;
        public EnvironmentVariableReplacementFailureMode EnvFailureMode { get; set; } = EnvironmentVariableReplacementFailureMode.Throw;

        // @env\('  : match @env('
        // @AKV\('  : match @AKV('
        // .*?      : lazy match any character except newline 0 or more times
        // (?='\))  : look ahead for ')' which will combine with our lazy match
        //            ie: in @env('hello')goodbye') we match @env('hello')
        // '\)      : consume the ') into the match (look ahead doesn't capture)
        // This pattern lazy matches any string that starts with @env(' and ends with ')
        // ie: fooBAR@env('hello-world')bash)FOO')  match: @env('hello-world')
        // This matching pattern allows for the @env('<match>') to be safely nested
        // within strings that contain ') after our match.
        // ie: if the environment variable "Baz" has the value of "Bar"
        // fooBarBaz: "('foo@env('Baz')Baz')" would parse into
        // fooBarBaz: "('fooBarBaz')"
        // Note that there is no escape character currently for ') to exist
        // within the name of the environment variable, but that ') is not
        // a valid environment variable name in certain shells.
        public const string OUTER_ENV_PATTERN = @"@env\('.*?(?='\))'\)";
        public const string OUTER_AKV_PATTERN = @"@AKV\('.*?(?='\))'\)";

        // [^@env\(]   :  any substring that is not @env(
        // [^@AKV\(]   :  any substring that is not @AKV(
        // .*          :  any char except newline any number of times
        // (?=\))      :  look ahead for end char of )
        // This pattern greedy matches all characters that are not a part of @env()
        // ie: @env('hello@env('goodbye')world') match: 'hello@env('goodbye')world'
        public const string INNER_ENV_PATTERN = @"[^@env\(].*(?=\))";
        public const string INNER_AKV_PATTERN = @"[^@AKV\(].*(?=\))";

        private readonly AzureKeyVaultOptions? _azureKeyVaultOptions;
        private readonly SecretClient? _akvClient;

        public Dictionary<Regex, Func<Match, string>> ReplacementStrategies { get; private set; } = new();

        public DeserializationVariableReplacementSettings(
            AzureKeyVaultOptions? azureKeyVaultOptions = null,
            bool doReplaceEnvVar = true,
            bool doReplaceAkvVar = true,
            EnvironmentVariableReplacementFailureMode envFailureMode = EnvironmentVariableReplacementFailureMode.Throw)
        {
            _azureKeyVaultOptions = azureKeyVaultOptions;
            DoReplaceEnvVar = doReplaceEnvVar;
            DoReplaceAkvVar = doReplaceAkvVar;
            EnvFailureMode = envFailureMode;

            if (DoReplaceEnvVar)
            {
                ReplacementStrategies.Add(
                    new Regex(OUTER_ENV_PATTERN, RegexOptions.Compiled),
                    ReplaceEnvVariable);
            }

            if (DoReplaceAkvVar && _azureKeyVaultOptions is not null)
            {
                _akvClient = CreateSecretClient(_azureKeyVaultOptions);
                ReplacementStrategies.Add(
                    new Regex(OUTER_AKV_PATTERN, RegexOptions.Compiled),
                    ReplaceAkvVariable);
            }
        }

        private string ReplaceEnvVariable(Match match)
        {
            // strips first and last characters, ie: '''hello'' --> ''hello'
            string name = Regex.Match(match.Value, INNER_ENV_PATTERN).Value[1..^1];
            string? value = Environment.GetEnvironmentVariable(name);
            if (EnvFailureMode is EnvironmentVariableReplacementFailureMode.Throw)
            {
                return value is not null ? value :
                    throw new DataApiBuilderException(
                        message: $"Environmental Variable, {name}, not found.",
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }
            else
            {
                return value ?? match.Value;
            }
        }

        private string ReplaceAkvVariable(Match match)
        {
            // strips first and last characters, ie: '''hello'' --> ''hello'
            string name = Regex.Match(match.Value, INNER_AKV_PATTERN).Value[1..^1];
            string? value = GetAKVVariable(name);
            if (EnvFailureMode == EnvironmentVariableReplacementFailureMode.Throw)
            {
                return value is not null ? value :
                    throw new DataApiBuilderException(message: $"Azure Key Vault Variable, {name}, not found.",
                                                   statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                                   subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }
            else
            {
                return value ?? match.Value;
            }
        }

        private static SecretClient CreateSecretClient(AzureKeyVaultOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.Endpoint))
            {
                throw new DataApiBuilderException(
                    "Azure Key Vault endpoint must be specified.",
                    System.Net.HttpStatusCode.InternalServerError,
                    DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            SecretClientOptions clientOptions = new();

            if (options.RetryPolicy is not null)
            {
                // Convert AKVRetryPolicyMode to RetryMode
                RetryMode retryMode = options.RetryPolicy.Mode switch
                {
                    AKVRetryPolicyMode.Fixed => RetryMode.Fixed,
                    AKVRetryPolicyMode.Exponential => RetryMode.Exponential,
                    null => RetryMode.Exponential,
                    _ => RetryMode.Exponential
                };

                clientOptions.Retry.Mode = retryMode;
                clientOptions.Retry.MaxRetries = options.RetryPolicy.MaxCount ?? 3;
                clientOptions.Retry.Delay = TimeSpan.FromSeconds(options.RetryPolicy.DelaySeconds ?? 1);
                clientOptions.Retry.MaxDelay = TimeSpan.FromSeconds(options.RetryPolicy.MaxDelaySeconds ?? 16);
                clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(options.RetryPolicy.NetworkTimeoutSeconds ?? 30);
            }

            return new SecretClient(new Uri(options.Endpoint), new DefaultAzureCredential(), clientOptions);
        }

        private string? GetAKVVariable(string name)
        {
            if (_akvClient is null)
            {
                throw new InvalidOperationException("Azure Key Vault client is not initialized.");
            }

            try
            {
                return _akvClient.GetSecret(name).Value.Value;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }
    }
}
