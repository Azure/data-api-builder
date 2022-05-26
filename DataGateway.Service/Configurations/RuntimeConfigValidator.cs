using System;
using System.IO;
using System.IO.Abstractions;
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
        private readonly IFileSystem _fileSystem;

        public RuntimeConfigValidator(
            IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath, IFileSystem fileSystem)
        {
            _runtimeConfig = runtimeConfigPath.CurrentValue.ConfigValue;
            _fileSystem = fileSystem;
        }

        public RuntimeConfigValidator(RuntimeConfig config)
        {
            _runtimeConfig = config;
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

            if (_runtimeConfig.DatabaseType == DatabaseType.cosmos)
            {
                if (_runtimeConfig.CosmosDb is null)
                {
                    throw new NotSupportedException("CosmosDB is specified but no CosmosDB configuration information has been provided.");
                }

                if (string.IsNullOrEmpty(_runtimeConfig.CosmosDb.GraphQLSchemaPath))
                {
                    throw new NotSupportedException("No GraphQL schema file has been provided for CosmosDB. Ensure you provide a GraphQL schema containing the GraphQL object types to expose.");
                }

                if (!_fileSystem.File.Exists(_runtimeConfig.CosmosDb.GraphQLSchemaPath))
                {
                    throw new FileNotFoundException($"The GraphQL schema file at '{_runtimeConfig.CosmosDb.GraphQLSchemaPath}' could not be found. Ensure that it is a path relative to run runtime.");
                }
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
