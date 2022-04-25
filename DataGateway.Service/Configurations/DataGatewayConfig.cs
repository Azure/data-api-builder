using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Config;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// Data gateway configuration.
    /// </summary>
    public class DataGatewayConfig
    {
        /*
         * This is an example of the configuration format
         *
         "DataGatewayConfig": {
            "DatabaseType": "",
            "ResolverConfigFile" : ""
            "ResolverConfig" : ""
            "RuntimeConfig" : ""
            "GraphQLSchema": ""
            "DatabaseConnection": {
                "ServerEndpointUrl": "",
                "AuthorizationKey": "",
                "Server": "",
                "Database": "",
                "Container": "",
                "ConnectionString": ""
              },
            "Authentication": {
                "Provider":"",
                "Audience":"",
                "Issuer":""
            }
         */
        public DatabaseType? DatabaseType { get; set; }

        // This should be renamed to databaseConnection but need to coordiate with moderakh on CI configuration.
        public DatabaseConnectionConfig DatabaseConnection { get; set; } = null!;
        public string? ResolverConfigFile { get; set; }
        public string? ResolverConfig { get; set; }
        public string? RuntimeConfigFile { get; set; }
        public string? GraphQLSchema { get; set; }
        public AuthenticationProviderConfig Authentication { get; set; } = null!;

        public static T GetDeserializedConfig<T>(string configJson)
        {
            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true,
            };
            options.Converters.Add(new JsonStringEnumConverter());

            // This feels verbose but it avoids having to make _config nullable - which would result in more
            // down the line issues and null check requirements
            T? deserializedConfig;
            if ((deserializedConfig = JsonSerializer.Deserialize<T>(configJson, options)) == null)
            {
                throw new JsonException("Failed to get a deserialized config from the provided config");
            }

            return deserializedConfig!;
        }
    }

    /// <summary>
    /// Database connection configuration.
    /// </summary>
    public class DatabaseConnectionConfig
    {
        public string ServerEndpointUrl { get; set; } = null!;
        public string AuthorizationKey { get; set; } = null!;
        public string Server { get; set; } = null!;
        public string Database { get; set; } = null!;
        public string Container { get; set; } = null!;
        public string ConnectionString { get; set; } = null!;
    }

    /// <summary>
    /// Jwt Auth Provider Config
    /// </summary>
    public class AuthenticationProviderConfig
    {
        //Type is Identity Provider such as AzureAD. If set to EasyAuth, no Audience, or issuer expected.
        public string Provider { get; set; } = null!;
        public string? Audience { get; set; }
        public string? Issuer { get; set; }
    }

    /// <summary>
    /// Post configuration processing for DataGatewayConfig.
    /// We check for database connection options. If the user does not provide connection string,
    /// we build a connection string from other connection settings and finally set the ConnectionString setting.
    ///
    /// This inteface is called before IValidateOptions. Hence, we need to do some validation here.
    /// </summary>
    public class DataGatewayConfigPostConfiguration : IPostConfigureOptions<DataGatewayConfig>
    {
        public void PostConfigure(string name, DataGatewayConfig options)
        {
            if (!options.DatabaseType.HasValue)
            {
                return;
            }

            bool connStringProvided = !string.IsNullOrEmpty(options.DatabaseConnection.ConnectionString);
            bool serverProvided = !string.IsNullOrEmpty(options.DatabaseConnection.Server);
            bool dbProvided = !string.IsNullOrEmpty(options.DatabaseConnection.Database);
            if (!connStringProvided && !serverProvided && !dbProvided)
            {
                throw new NotSupportedException("Either Server and Database or ConnectionString need to be provided");
            }
            else if (connStringProvided && (serverProvided || dbProvided))
            {
                throw new NotSupportedException("Either Server and Database or ConnectionString need to be provided, not both");
            }

            bool isResolverConfigSet = !string.IsNullOrEmpty(options.ResolverConfig);
            bool isResolverConfigFileSet = !string.IsNullOrEmpty(options.ResolverConfigFile);
            bool isGraphQLSchemaSet = !string.IsNullOrEmpty(options.GraphQLSchema);
            bool isRuntimeConfigFileSet = !string.IsNullOrEmpty(options.RuntimeConfigFile);
            if (!(isResolverConfigSet ^ isResolverConfigFileSet))
            {
                throw new NotSupportedException("Either the Resolver Config or the Resolver Config File needs to be provided. Not both.");
            }

            if (isResolverConfigSet && !isGraphQLSchemaSet)
            {
                throw new NotSupportedException("The GraphQLSchema should be provided with the config.");
            }

            if (!isRuntimeConfigFileSet)
            {
                throw new NotSupportedException("The Runtime Config File needs to be provided.");
            }

            if (string.IsNullOrWhiteSpace(options.DatabaseConnection.ConnectionString))
            {
                if ((!serverProvided && dbProvided) || (serverProvided && !dbProvided))
                {
                    throw new NotSupportedException("Both Server and Database need to be provided");
                }

                SqlConnectionStringBuilder builder = new()
                {
                    InitialCatalog = options.DatabaseConnection.Database,
                    DataSource = options.DatabaseConnection.Server,

                    IntegratedSecurity = true
                };
                options.DatabaseConnection.ConnectionString = builder.ToString();
            }

            ValidateAuthenticationConfig(options);
        }

        private static void ValidateAuthenticationConfig(DataGatewayConfig options)
        {
            bool isAuthenticationTypeSet = !string.IsNullOrEmpty(options.Authentication.Provider);
            bool isAudienceSet = !string.IsNullOrEmpty(options.Authentication.Audience);
            bool isIssuerSet = !string.IsNullOrEmpty(options.Authentication.Issuer);
            if (!isAuthenticationTypeSet)
            {
                throw new NotSupportedException("Authentication.Provider must be defined.");
            }
            else
            {
                if (options.Authentication.Provider != "EasyAuth" && (!isAudienceSet || !isIssuerSet))
                {
                    throw new NotSupportedException("Audience and Issuer must be set when not using EasyAuth.");
                }

                if (options.Authentication.Provider == "EasyAuth" && (isAudienceSet || isIssuerSet))
                {
                    throw new NotSupportedException("Audience and Issuer should not be set and are not used with EasyAuth.");
                }
            }
        }
    }

    /// <summary>
    /// Validate config.
    /// This happens after post configuration.
    /// </summary>
    public class DataGatewayConfigValidation : IValidateOptions<DataGatewayConfig>
    {
        public ValidateOptionsResult Validate(string name, DataGatewayConfig options)
        {
            if (!options.DatabaseType.HasValue)
            {
                return ValidateOptionsResult.Success;
            }

            return string.IsNullOrWhiteSpace(options.DatabaseConnection.ConnectionString)
                ? ValidateOptionsResult.Fail("Invalid connection string.")
                : ValidateOptionsResult.Success;
        }
    }
}
