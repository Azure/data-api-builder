using System;
using System.IO;
using Azure.DataGateway.Config;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// This class encapsulates methods to validate the runtime config file.
    /// </summary>
    public class RuntimeConfigValidator : IConfigValidator
    {
        private readonly RuntimeConfig? _runtimeConfig;

        public RuntimeConfigValidator(RuntimeConfig runtimeConfig)
        {
            _runtimeConfig = runtimeConfig;
        }

        /// <summary>
        /// The driver for validation of the runtime configuration file.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public void ValidateConfig()
        {
            if (_runtimeConfig is null)
            {
                throw new ArgumentNullException("hawaii-config",
                    "The runtime configuration value has not been set yet.");
            }

            if (string.IsNullOrWhiteSpace(_runtimeConfig.DatabaseType.ToString()))
            {
                throw new NotSupportedException("The database-type should be provided with the runtime config.");
            }

            if (string.IsNullOrWhiteSpace(_runtimeConfig.ConnectionString))
            {
                throw new NotSupportedException($"The Connection String should be provided.");
            }

            if (_runtimeConfig.DatabaseType.Equals(DatabaseType.cosmos) &&
                ((_runtimeConfig.CosmosDb is null) ||
                (string.IsNullOrWhiteSpace(_runtimeConfig.CosmosDb.ResolverConfigFile)) ||
                (!File.Exists(_runtimeConfig.CosmosDb.ResolverConfigFile))))
            {
                throw new NotSupportedException("The resolver-config-file should be provided with the runtime config and must exist in the current directory when database type is cosmosdb.");
            }

            ValidateAuthenticationConfig();
        }

        private void ValidateAuthenticationConfig()
        {
            bool isAudienceSet =
                _runtimeConfig!.AuthNConfig is not null &&
                _runtimeConfig!.AuthNConfig.Jwt is not null &&
                !string.IsNullOrEmpty(_runtimeConfig!.AuthNConfig.Jwt.Audience);
            bool isIssuerSet =
                _runtimeConfig!.AuthNConfig is not null &&
                _runtimeConfig!.AuthNConfig.Jwt is not null &&
                !string.IsNullOrEmpty(_runtimeConfig!.AuthNConfig.Jwt.Issuer);
            if (!_runtimeConfig!.IsEasyAuthAuthenticationProvider() && (!isAudienceSet || !isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer must be set when not using EasyAuth.");
            }

            if (_runtimeConfig!.IsEasyAuthAuthenticationProvider() && (isAudienceSet || isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer should not be set and are not used with EasyAuth.");
            }
        }
    }
}
