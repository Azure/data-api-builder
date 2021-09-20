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
            if (instance == null)
            {
                throw new Exception("Configuration has not been initialized yet");
            }

            return instance;
        }

        public static void init(IConfiguration config)
        {
            if (instance != null)
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

            instance.ConnectionString = section.GetValue<string>("Credentials:ConnectionString");

            if (string.IsNullOrEmpty(instance.ConnectionString) && instance.DbType == DatabaseType.MsSql) {
                var server = section.GetValue<string>("Credentials:Server");
                if (string.IsNullOrEmpty(instance.ConnectionString)) {
                    throw new NotSupportedException("Either ConnectionString or Server needs to be provided");
                }

                var dbName = section.GetValue<string>("Credentials:DatabaseName");
                if (string.IsNullOrEmpty(instance.ConnectionString)) {
                    throw new NotSupportedException("Either ConnectionString or DatabaseName needs to be provided");
                }
                var builder = new SqlConnectionStringBuilder
                {
                    InitialCatalog = dbName,
                    DataSource = server,
                };

                builder.IntegratedSecurity = true;
                instance.ConnectionString = builder.ToString();
            }

            if (string.IsNullOrEmpty(instance.ConnectionString)) {
                throw new NotSupportedException("ConnectionString needs to be provided");
            }

            instance.ResolverConfigFile = config.GetValue("ResolverConfigFile", "config.json");
        }
    }
}
