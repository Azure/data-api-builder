using System;
using System.IO;
using System.IO.Abstractions;
using Azure.DataGateway.Config;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// This class encapsulates methods to validate the runtime config file.
    /// </summary>
    public class RuntimeConfigValidator : IConfigValidator
    {
        private readonly IFileSystem _fileSystem;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;

        public RuntimeConfigValidator(RuntimeConfigProvider runtimeConfigProvider, IFileSystem fileSystem)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
            _fileSystem = fileSystem;
        }

        /// <summary>
        /// The driver for validation of the runtime configuration file.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public void ValidateConfig()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetRuntimeConfiguration();

            if (string.IsNullOrWhiteSpace(runtimeConfig.DatabaseType.ToString()))
            {
                throw new NotSupportedException("The database-type should be provided with the runtime config.");
            }

            if (string.IsNullOrWhiteSpace(runtimeConfig.ConnectionString))
            {
                throw new NotSupportedException($"The Connection String should be provided.");
            }

            if (runtimeConfig.DatabaseType == DatabaseType.cosmos)
            {
                if (runtimeConfig.CosmosDb is null)
                {
                    throw new NotSupportedException("CosmosDB is specified but no CosmosDB configuration information has been provided.");
                }

                if (string.IsNullOrEmpty(runtimeConfig.CosmosDb.GraphQLSchemaPath))
                {
                    throw new NotSupportedException("No GraphQL schema file has been provided for CosmosDB. Ensure you provide a GraphQL schema containing the GraphQL object types to expose.");
                }

                if (!_fileSystem.File.Exists(runtimeConfig.CosmosDb.GraphQLSchemaPath))
                {
                    throw new FileNotFoundException($"The GraphQL schema file at '{runtimeConfig.CosmosDb.GraphQLSchemaPath}' could not be found. Ensure that it is a path relative to the runtime.");
                }
            }

            ValidateAuthenticationConfig();
        }

        private void ValidateAuthenticationConfig()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetRuntimeConfiguration();

            bool isAudienceSet =
                runtimeConfig.AuthNConfig is not null &&
                runtimeConfig.AuthNConfig.Jwt is not null &&
                !string.IsNullOrEmpty(runtimeConfig.AuthNConfig.Jwt.Audience);
            bool isIssuerSet =
                runtimeConfig.AuthNConfig is not null &&
                runtimeConfig.AuthNConfig.Jwt is not null &&
                !string.IsNullOrEmpty(runtimeConfig.AuthNConfig.Jwt.Issuer);
            if (!runtimeConfig.IsEasyAuthAuthenticationProvider() && (!isAudienceSet || !isIssuerSet))
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
