// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using Azure.Core;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging; // added for logging malformed .akv lines

namespace Azure.DataApiBuilder.Config
{
    public class DeserializationVariableReplacementSettings
    {
        public bool DoReplaceEnvVar { get; set; }
        public bool DoReplaceAkvVar { get; set; }
        public EnvironmentVariableReplacementFailureMode EnvFailureMode { get; set; } = EnvironmentVariableReplacementFailureMode.Throw;

        // @env\('  : match @env('
        // @akv\('  : match @akv('
        // .*?      : lazy match any character except newline 0 or more times
        // (?='\))  : look ahead for ')' which will combine with our lazy match
        //            ie: in @env('hello')goodbye') we match @env('hello')
        // '\)      : consume the ') into the match (look ahead doesn't capture)
        // This pattern lazy matches any string that starts with @env(' and ends with ') OR @akv(' and ends with ')
        // Example: fooBAR@env('hello-world')bash)FOO')  match: @env('hello-world')
        // Example: fooBAR@akv('secret-name')bash)FOO') match: @akv('secret-name')
        // This matching pattern allows for the @env('<match>') / @akv('<match>') to be safely nested
        // within strings that contain ')' after our match.
        // Note that there is no escape character currently for ')' to exist within the name of the variable.
        public const string OUTER_ENV_PATTERN = @"@env\('.*?(?='\))'\)";
        public const string OUTER_AKV_PATTERN = @"@akv\('.*?(?='\))'\)";

        // [^@env\(]   :  any substring that is not @env(
        // [^@akv\(]   :  any substring that is not @akv(
        // .*          :  any char except newline any number of times
        // (?=\))      :  look ahead for end char of )
        // This pattern greedy matches all characters that are not a part of @env() / @akv()
        // ie: @env('hello@env('goodbye')world') match: 'hello@env('goodbye')world'
        public const string INNER_ENV_PATTERN = @"[^@env\(].*(?=\))";
        public const string INNER_AKV_PATTERN = @"[^@akv\(].*(?=\))";

        private readonly AzureKeyVaultOptions? _azureKeyVaultOptions;
        private readonly SecretClient? _akvClient;
        private readonly Dictionary<string, string>? _akvFileSecrets;
        private readonly ILogger? _logger;

        public Dictionary<Regex, Func<Match, string>> ReplacementStrategies { get; private set; } = new();

        public DeserializationVariableReplacementSettings(
            AzureKeyVaultOptions? azureKeyVaultOptions = null,
            bool doReplaceEnvVar = false,
            bool doReplaceAkvVar = false,
            EnvironmentVariableReplacementFailureMode envFailureMode = EnvironmentVariableReplacementFailureMode.Throw,
            ILogger? logger = null)
        {
            _azureKeyVaultOptions = azureKeyVaultOptions;
            DoReplaceEnvVar = doReplaceEnvVar;
            DoReplaceAkvVar = doReplaceAkvVar;
            EnvFailureMode = envFailureMode;
            _logger = logger;

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
                    _akvFileSecrets = LoadAkvFileSecrets(_azureKeyVaultOptions.Endpoint!, _logger);
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
        private static Dictionary<string, string> LoadAkvFileSecrets(string filePath, ILogger? logger = null)
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
                    logger?.LogDebug("Ignoring malformed line in AKV secrets file {FilePath}: {Line}", filePath, rawLine);
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
                    if (!secrets.TryAdd(key, value))
                    {
                        logger?.LogDebug("Duplicate key '{Key}' encountered in AKV secrets file {FilePath}. Skipping later value.", key, filePath);
                    }
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

            // Validate AKV secret name per rules:
            // Allowed: alphanumeric and hyphen (-)
            // Disallowed: spaces or any other symbols
            // Must start and end with alphanumeric
            // Length: 1 to 127 chars
            if (!IsValidAkvSecretName(name, out string validationError))
            {
                throw new DataApiBuilderException(
                    message: $"Azure Key Vault secret name '{name}' is invalid. {validationError} Requirements: allowed characters are alphanumeric and hyphen (-); must start and end with an alphanumeric character; length 1-127 characters; case-insensitive.",
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

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

        private static bool IsValidAkvSecretName(string name, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                error = "Name cannot be null or empty.";
                return false;
            }

            if (name.Length < 1 || name.Length > 127)
            {
                error = $"Length {name.Length} is outside allowed range (1-127).";
                return false;
            }

            // Must start and end with alphanumeric
            if (!char.IsLetterOrDigit(name[0]) || !char.IsLetterOrDigit(name[^1]))
            {
                error = "Must start and end with an alphanumeric character.";
                return false;
            }

            // Allowed characters: letters, digits, hyphen.
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (!(char.IsLetterOrDigit(c) || c == '-'))
                {
                    error = $"Invalid character '{c}' at position {i}.";
                    return false;
                }
            }

            return true;
        }

        private static SecretClient CreateSecretClient(AzureKeyVaultOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.Endpoint))
            {
                throw new DataApiBuilderException(
                    "Missing 'endpoint' property is required to connect to Azure Key Vault.",
                    System.Net.HttpStatusCode.InternalServerError,
                    DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            // If endpoint is a local .akv file, we should not create a SecretClient.
            if (IsLocalAkvFileEndpoint(options.Endpoint))
            {
                throw new DataApiBuilderException(
                    "Attempted to create Azure Key Vault client for local .akv file endpoint.",
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
