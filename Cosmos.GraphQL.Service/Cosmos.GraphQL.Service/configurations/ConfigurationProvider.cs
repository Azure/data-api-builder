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
        private static readonly object lockObject = new object();
        public IDatabaseCredentials Creds { get; private set; }

        /// <summary>
        /// Determines the type of Db this app targets.
        /// </summary>
        public DatabaseType DbType { get; set; }

        public static ConfigurationProvider getInstance()
        {
            if (instance == null)
            {
                lock (lockObject)
                {
                    if (instance == null)
                    {
                        var myInstance = new ConfigurationProvider();
                        myInstance.init();
                        instance = myInstance;
                    }
                }
            }

            return instance;
        }
        
        private void init()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("AppSettings.json").Build();

            var section = config.GetSection("DatabaseConnection");
            if (Enum.TryParse<DatabaseType>(section["DatabaseType"], out DatabaseType dbType))
            {
                DbType = dbType;
            }
            else
            {
                throw new NotSupportedException(String.Format("The configuration file is invalid and does not contain a *valid* DatabaseType key."));
            }

            section = section.GetSection("Credentials");
            switch (DbType)
            {
                case DatabaseType.Cosmos:
                    Creds = section.Get<CosmosCredentials>();
                    break;
                case DatabaseType.MsSql:
                    Creds = section.Get<MsSqlCredentials>();
                    break;
                default:
                    break;
            }
        }
    }
}