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

    /// <summary>
    /// Processes appsettings.json file containing dbtype
    /// and database connection credentials/connections strings.
    /// </summary>
    public class ConfigurationProvider
    {
        private static ConfigurationProvider instance;

        /// <summary>
        /// Determines the type of Db this app targets.
        /// </summary>
        public DatabaseType DbType { get; private set; }

        /// <summary>
        /// Determines the filename of the resolver config.
        /// </summary>
        public string ResolverConfigFile { get; private set; }

        /// <summary>
        /// Determines the connectionstring that should be used to connect to
        /// the database.
        /// </summary>
        public string ConnectionString { get; private set; }

        public static ConfigurationProvider getInstance()
        {
            if (!Initialized())
            {
                throw new Exception("Configuration has not been initialized yet");
            }

            return instance;
        }

        public static bool Initialized()
        {
            return instance != null;
        }

        /// <summary>
        /// Builds the connection string for MsSql based on the
        /// ConnectionString field or the Server+DatabaseName fields.
        /// </summary>
        private static string BuildMsSqlConnectionString(IConfigurationSection credentialSection)
        {
            var connString = credentialSection.GetValue<string>("ConnectionString");
            var server = credentialSection.GetValue<string>("Server");
            var dbName = credentialSection.GetValue<string>("DatabaseName");
            var connStringProvided = !string.IsNullOrEmpty(connString);
            var serverProvided = !string.IsNullOrEmpty(server);
            var dbNameProvided = !string.IsNullOrEmpty(dbName);

            if (connStringProvided && (serverProvided || dbNameProvided))
            {
                throw new NotSupportedException("Either Server and DatabaseName or ConnectionString need to be provided, not both");
            }
            if (!connStringProvided && !serverProvided && !dbNameProvided)
            {
                throw new NotSupportedException("Either Server and DatabaseName or ConnectionString need to be provided");
            }

            if (connStringProvided)
            {
                return connString;
            }

            if ((!serverProvided && dbNameProvided) || (serverProvided && !dbNameProvided))
            {
                throw new NotSupportedException("Both Server and DatabaseName need to be provided");
            }

            var builder = new SqlConnectionStringBuilder
            {
                InitialCatalog = dbName,
                DataSource = server,
            };

            builder.IntegratedSecurity = true;
            return builder.ToString();

        }

        public static void init(IConfiguration config)
        {
            if (Initialized())
            {
                throw new Exception("Configuration provider can only be initialized once");
            }

            instance = new ConfigurationProvider();

            var connectionSection = config.GetSection("DatabaseConnection");
            if (Enum.TryParse<DatabaseType>(connectionSection["DatabaseType"], out DatabaseType dbType))
            {
                instance.DbType = dbType;
            }
            else
            {
                throw new NotSupportedException(String.Format("The configuration file is invalid and does not contain a *valid* DatabaseType key."));
            }

            var credentialSection = connectionSection.GetSection("Credentials");
            if (instance.DbType == DatabaseType.MsSql)
            {
                instance.ConnectionString = BuildMsSqlConnectionString(credentialSection);
            }
            else
            {
                instance.ConnectionString = credentialSection.GetValue<string>("ConnectionString");
                if (string.IsNullOrEmpty(instance.ConnectionString))
                {
                    throw new NotSupportedException("ConnectionString needs to be provided");
                }
            }


            instance.ResolverConfigFile = config.GetValue("ResolverConfigFile", "config.json");
        }
    }
}
