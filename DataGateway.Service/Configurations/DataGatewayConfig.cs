using System;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// The different type of databases this app supports.
    /// </summary>
    public enum DatabaseType
    {
        Cosmos,
        MsSql,
        PostgreSql,
        MySql
    }
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
            "GraphQLSchema": ""
            "DatabaseConnection": {
                "ServerEndpointUrl": "",
                "AuthorizationKey": "",
                "Server": "",
                "Database": "",
                "Container": "",
                "ConnectionString": ""
              }
            }
         */
        public DatabaseType? DatabaseType { get; set; }

        // This should be renamed to databaseConnection but need to coordiate with moderakh on CI configuration.
        public DatabaseConnectionConfig DatabaseConnection { get; set; } = null!;
        public string? ResolverConfigFile { get; set; }
        public string? ResolverConfig { get; set; }
        public string? GraphQLSchema { get; set; }
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
            if (!(isResolverConfigSet ^ isResolverConfigFileSet))
            {
                throw new NotSupportedException("Either the Resolver Config or the Resolver Config File needs to be provided. Not both.");
            }

            if (isResolverConfigSet && !isGraphQLSchemaSet)
            {
                throw new NotSupportedException("The GraphQLSchema should be provided with the config.");
            }

            if (string.IsNullOrWhiteSpace(options.DatabaseConnection.ConnectionString))
            {
                if ((!serverProvided && dbProvided) || (serverProvided && !dbProvided))
                {
                    throw new NotSupportedException("Either Server and Database or ConnectionString need to be provided");
                }
                else if (connStringProvided && (serverProvided || dbProvided))
                {
                    throw new NotSupportedException("Either Server and Database or ConnectionString need to be provided, not both");
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
                    };

                    builder.IntegratedSecurity = true;
                    options.DatabaseConnection.ConnectionString = builder.ToString();
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
