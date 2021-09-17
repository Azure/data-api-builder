using System;
using Microsoft.Extensions.Configuration;

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
        public IDatabaseCredentials Creds { get; private set; }

        /// <summary>
        /// Determines the type of Db this app targets.
        /// </summary>
        public DatabaseType DbType { get; private set; }

        /// <summary>
        /// Determines the filename of the resolver config.
        /// </summary>
        public string ResolverConfigFile { get; private set; }

        public static ConfigurationProvider getInstance()
        {
            if (!Initialized())
            {
                throw new Exception("Configuration has not been initialized yet");
            }

            return instance;
        }

        public static bool Initialized() {
            return instance != null;
        }

        public static void init(IConfiguration config)
        {
            if (Initialized())
            {
                throw new Exception("Configuration provider can only be initialized once");
            }

            instance = new ConfigurationProvider();

            var section = config.GetSection("DatabaseConnection");
            if (Enum.TryParse<DatabaseType>(section["DatabaseType"], out DatabaseType dbType))
            {
                instance.DbType = dbType;
            }
            else
            {
                throw new NotSupportedException(String.Format("The configuration file is invalid and does not contain a *valid* DatabaseType key."));
            }

            section = section.GetSection("Credentials");
            switch (instance.DbType)
            {
                case DatabaseType.Cosmos:
                    instance.Creds = section.Get<CosmosCredentials>();
                    break;
                case DatabaseType.MsSql:
                    instance.Creds = section.Get<MsSqlCredentials>();
                    break;
                case DatabaseType.PostgreSql:
                    instance.Creds = section.Get<PostgresCredentials>();
                    break;
            }

            instance.ResolverConfigFile = config.GetValue("ResolverConfigFile", "config.json");
        }
    }
}
