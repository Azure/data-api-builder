using System.Configuration;
using System;
using Microsoft.Extensions.Configuration;

namespace Cosmos.GraphQL.Service.configurations
{
    public class ConfigurationProvider
    {
        private static ConfigurationProvider instance; 
        private static readonly object lockObject = new object();
        
        public CosmosCredentials cred { get; private set; }
        public string databaseName { get; private set; }
        public string containerName { get; private set;  }


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
                .AddJsonFile("config.json").Build();

            var section = config.GetSection(nameof(CosmosCredentials));
            cred = section.Get<CosmosCredentials>();
            containerName = config.GetValue<string>("CosmosContainer");
            databaseName = config.GetValue<string>("CosmosDatabase");
        }
    }

    public class CosmosCredentials
    {
        public string EndpointUrl { get; set; }
        public string AuthorizationKey { get; set; }
    }
}