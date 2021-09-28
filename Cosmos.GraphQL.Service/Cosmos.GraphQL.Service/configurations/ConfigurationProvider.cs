using System;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Cosmos.GraphQL.Service.configurations
{
    /// <summary>
    /// The different type of databases this app supports.
    /// </summary>
    public enum DatabaseType
    {
        Cosmos,
        MsSql,
        PostgreSql,
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

        public DatabaseType DatabaseType { get; set; }

        // This should be renamed to databaseConnection but need to coordiate with moderakh on CI configuration.
        public DatabaseConnectionConfig Credentials { get; set; }
        public string ResolverConfigFile { get; set; } = "config.json";
    }

    /// <summary>
    /// Database connection configuration.
    /// </summary>
    public class DatabaseConnectionConfig
    {
        public string ServerEndpointUrl { get; set; }
        public string AuthorizationKey { get; set; }
        public string Server { get; set; }
        public string Database { get; set; }
        public string Container { get; set; }
        public string ConnectionString { get; set; }
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
            bool connStringProvided = !string.IsNullOrEmpty(options.Credentials.ConnectionString);
            bool serverProvided = !string.IsNullOrEmpty(options.Credentials.Server);
            bool dbNameProvided = !string.IsNullOrEmpty(options.Credentials.Database);

            if (!connStringProvided && !serverProvided && !dbNameProvided)
            {
                throw new NotSupportedException("Either Server and DatabaseName or ConnectionString need to be provided");
            }
            else if (connStringProvided && (serverProvided || dbNameProvided))
            {
                throw new NotSupportedException("Either Server and DatabaseName or ConnectionString need to be provided, not both");
            }

            if (string.IsNullOrWhiteSpace(options.Credentials.ConnectionString))
            {
                if ((!serverProvided && dbNameProvided) || (serverProvided && !dbNameProvided))
                {
                    throw new NotSupportedException("Both Server and DatabaseName need to be provided");
                }

                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
                {
                    InitialCatalog = options.Credentials.Database,
                    DataSource = options.Credentials.Server,
                };

                builder.IntegratedSecurity = true;
                options.Credentials.ConnectionString = builder.ToString();
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
            return string.IsNullOrWhiteSpace(options.Credentials.ConnectionString)
                ? ValidateOptionsResult.Fail("Invalid connection string.")
                : ValidateOptionsResult.Success;
        }
    }
}
