using System;
using System.IO;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// This class encapsulates methods to validate the runtime config file.
    /// </summary>
    public class RuntimeConfigValidator : IConfigValidator
    {
        private readonly RuntimeConfigProvider _runtimeConfigProvider;

        public RuntimeConfigValidator(RuntimeConfigProvider runtimeConfigProvider)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
        }

        /// <summary>
        /// The driver for validation of the runtime configuration file.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public void ValidateConfig()
        {
            RuntimeConfig? runtimeConfig = _runtimeConfigProvider.RuntimeConfiguration;
            if (runtimeConfig is null)
            {
                throw new ArgumentNullException("hawaii-config",
                    "The runtime configuration value has not been set yet.");
            }

            if (string.IsNullOrWhiteSpace(runtimeConfig.DatabaseType.ToString()))
            {
                throw new NotSupportedException("The database-type should be provided with the runtime config.");
            }

            if (string.IsNullOrWhiteSpace(runtimeConfig.ConnectionString))
            {
                throw new NotSupportedException($"The Connection String should be provided.");
            }

            if (runtimeConfig.DatabaseType.Equals(DatabaseType.cosmos))
            {
                ResolverConfig? resolverConfig = _runtimeConfigProvider.ResolverConfig;
                if ((resolverConfig is null) && ((runtimeConfig.CosmosDb is null) ||
                    (string.IsNullOrWhiteSpace(runtimeConfig.CosmosDb.ResolverConfigFile)) ||
                    (!File.Exists(runtimeConfig.CosmosDb.ResolverConfigFile))))
                {
                    throw new NotSupportedException("The ResolverConfig should be set or the resolver-config-file should be" +
                        " provided with the runtime config and must exist in the current directory when database type is cosmosdb.");
                }
            }

            ValidateAuthenticationConfig();
        }

        private void ValidateAuthenticationConfig()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.RuntimeConfiguration!;

            bool isAudienceSet =
                runtimeConfig!.AuthNConfig is not null &&
                runtimeConfig!.AuthNConfig.Jwt is not null &&
                !string.IsNullOrEmpty(runtimeConfig!.AuthNConfig.Jwt.Audience);
            bool isIssuerSet =
                runtimeConfig!.AuthNConfig is not null &&
                runtimeConfig!.AuthNConfig.Jwt is not null &&
                !string.IsNullOrEmpty(runtimeConfig!.AuthNConfig.Jwt.Issuer);
            if (!runtimeConfig!.IsEasyAuthAuthenticationProvider() && (!isAudienceSet || !isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer must be set when not using EasyAuth.");
            }

            if (runtimeConfig!.IsEasyAuthAuthenticationProvider() && (isAudienceSet || isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer should not be set and are not used with EasyAuth.");
            }
        }
    }
}
