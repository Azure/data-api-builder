using System.Configuration;
using System;
using Microsoft.Extensions.Configuration;

namespace Cosmos.GraphQL.Service.configurations
{
    public static class ConfigurationProvider
    {
        private static CosmosCredentials cred;
        private static string databaseName;
        private static string containerName;
        private static readonly object syncLock = new object();

        private static void init()
        {
            
            
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("config.json").Build();

            var section = config.GetSection(nameof(CosmosCredentials));
            cred = section.Get<CosmosCredentials>();
            containerName = config.GetValue<string>("CosmosContainer");
            databaseName = config.GetValue<string>("CosmosDatabase");
        }
        
        public static CosmosCredentials getCosmosCredentials()
        {
            if (cred == null)
            {
                lock (syncLock)
                {
                    if (cred == null)
                    {
                        init();
                    }
                }
            }
            
            // assert cred != null
            return cred;
        }


        public static string getDatabaseName()
        {
            return databaseName;
        }
        
        
        public static string getContainer()
        {
            return containerName;
        }
    }

    public class CosmosCredentials
    {
        
        public string EndpointUrl { get; set; }
        public string AuthorizationKey { get; set; }
    }
}