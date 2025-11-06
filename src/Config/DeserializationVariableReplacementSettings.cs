// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using Azure.Core;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace Azure.DataApiBuilder.Config
{
    public class DeserializationVariableReplacementSettings
    {
        public bool DoReplaceEnvVar { get; set; }
        public bool DoReplaceAkvVar { get; set; }
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
        private readonly Dictionary<string, string>? _akvFileSecrets; // Local .akv file secrets

        public Dictionary<Regex, Func<Match, string>> ReplacementStrategies { get; private set; } = new();

        public DeserializationVariableReplacementSettings(
            AzureKeyVaultOptions? azureKeyVaultOptions = null,
            bool doReplaceEnvVar = false,
            bool doReplaceAkvVar = false,
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
                // Determine if endpoint points to a local .akv file. If so, load secrets from file; otherwise, use remote AKV.
                if (IsLocalAkvFileEndpoint(_azureKeyVaultOptions.Endpoint))
                {
                    _akvFileSecrets = LoadAkvFileSecrets(_azureKeyVaultOptions.Endpoint!);
                }
                else
                {
                    _akvClient = CreateSecretClient(_azureKeyVaultOptions);
                }

                ReplacementStrategies.Add(
                    new Regex(OUTER_AKV_PATTERN, RegexOptions.Compiled),
                    ReplaceAkvVariable);
            }
        }

        // Checks if the endpoint is a path to a local .akv file.
        private static bool IsLocalAkvFileEndpoint(string? endpoint)
            => !string.IsNullOrWhiteSpace(endpoint)
               && endpoint.EndsWith(".akv", StringComparison.OrdinalIgnoreCase)
               && File.Exists(endpoint);

        // Loads key=value pairs from a .akv file, similar to .env style. Lines starting with '#' are comments.
        private static Dictionary<string, string> LoadAkvFileSecrets(string filePath)
        {
            Dictionary<string, string> secrets = new(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in File.ReadAllLines(filePath))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                {
                    continue;
                }

                int eqIndex = line.IndexOf('=');
                if (eqIndex <= 0)
                {
                    // Ignore malformed lines silently; could be enhanced to log.
                    continue;
                }

                string key = line.Substring(0, eqIndex).Trim();
                string value = line[(eqIndex + 1)..].Trim();

                // Remove optional surrounding quotes
                if (value.Length >= 2 && ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\''))))
                {
                    value = value[1..^1];
                }

                if (!string.IsNullOrEmpty(key))
                {
                    secrets[key] = value;
                }
            }
            return secrets;
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
            string? value = GetAkvVariable(name);
            if (EnvFailureMode == EnvironmentVariableReplacementFailureMode.Throw)
            {
                return value is not null ? value :
                    throw new DataApiBuilderException(message: $"Azure Key Vault Variable, '{name}', not found.",
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
            // If endpoint is a local .akv file, we should not create a SecretClient.
            if (IsLocalAkvFileEndpoint(options.Endpoint))
            {
                throw new DataApiBuilderException(
                    "Attempted to create Azure Key Vault client for local .akv file endpoint.",
                    System.Net.HttpStatusCode.InternalServerError,
                    DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            if (string.IsNullOrWhiteSpace(options.Endpoint))
            {
                throw new DataApiBuilderException(
                    "Missing 'endpoint' property is required to connect to Azure Key Vault.",
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
                clientOptions.Retry.MaxRetries = options.RetryPolicy.MaxCount ?? AKVRetryPolicyOptions.DEFAULT_MAX_COUNT;
                clientOptions.Retry.Delay = TimeSpan.FromSeconds(options.RetryPolicy.DelaySeconds ?? AKVRetryPolicyOptions.DEFAULT_DELAY_SECONDS);
                clientOptions.Retry.MaxDelay = TimeSpan.FromSeconds(options.RetryPolicy.MaxDelaySeconds ?? AKVRetryPolicyOptions.DEFAULT_MAX_DELAY_SECONDS);
                clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(options.RetryPolicy.NetworkTimeoutSeconds ?? AKVRetryPolicyOptions.DEFAULT_NETWORK_TIMEOUT_SECONDS);
            }

            return new SecretClient(new Uri(options.Endpoint), new DefaultAzureCredential(), clientOptions);
        }

        private string? GetAkvVariable(string name)
        {
            // If using local .akv file secrets, return from dictionary.
            if (_akvFileSecrets is not null)
            {
                return _akvFileSecrets.TryGetValue(name, out string? value) ? value : null;
            }

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
