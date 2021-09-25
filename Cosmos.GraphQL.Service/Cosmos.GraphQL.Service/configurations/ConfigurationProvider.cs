using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;

namespace Cosmos.GraphQL.Service.configurations
{
    /// <summary>
    /// The different Dbs this app supports.
    /// </summary>
    public enum DatabaseType
    {
        Cosmos,
        MsSql,
        PostgreSql,
    }

    public class DatabaseConnection
    {
        /*
         "DatabaseConnection": {
            "DatabaseType": "",
            "Credentials": {
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
        public CredentialConfig Credentials { get; set; }
        public string ResolverConfigFile { get; set; }
    }

    public class CredentialConfig
    {
        public string ServerEndpointUrl { get; set; }
        public string AuthorizationKey { get; set; }
        public string Server { get; set; }
        public string Database { get; set; }
        public string Container { get; set; }
        public string ConnectionString { get; set; }

        public string GetConnectionString()
        {
            var connStringProvided = !string.IsNullOrEmpty(ConnectionString);
            var serverProvided = !string.IsNullOrEmpty(Server);
            var dbNameProvided = !string.IsNullOrEmpty(Database);

            if(connStringProvided && (serverProvided || dbNameProvided))
            {
                throw new NotSupportedException("Either Server and DatabaseName or ConnectionString need to be provided, not both");
            }

            if(!connStringProvided && !serverProvided && !dbNameProvided)
            {
                throw new NotSupportedException("Either Server and DatabaseName or ConnectionString need to be provided");
            }

            if(connStringProvided)
            {
                return ConnectionString;
            }

            if((!serverProvided && dbNameProvided) || (serverProvided && !dbNameProvided))
            {
                throw new NotSupportedException("Both Server and DatabaseName need to be provided");
            }

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
            {
                InitialCatalog = Database,
                DataSource = Database,
            };

            builder.IntegratedSecurity = true;
            return builder.ToString();
        }
    }
}
