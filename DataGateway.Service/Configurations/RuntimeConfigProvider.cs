using System;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Models;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Configurations
{
    public class RuntimeConfigProvider
    {
        public event EventHandler<RuntimeConfig>? RuntimeConfigLoaded;

        public virtual RuntimeConfig? RuntimeConfiguration { get; internal set; }
        public ResolverConfig? ResolverConfig { get; internal set; }

        public RuntimeConfigProvider() { }

        public RuntimeConfigProvider(IOptions<RuntimeConfigPath>? runtimeConfigPath)
        {
            if (runtimeConfigPath != null)
            {
                RuntimeConfiguration = runtimeConfigPath.Value.LoadRuntimeConfigValue();
            }
        }

        public void Initialize(string configuration, string schema, string connectionString, string? resolvers)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            RuntimeConfiguration = RuntimeConfig.GetDeserializedConfig<RuntimeConfig>(configuration);
            RuntimeConfiguration.DetermineGlobalSettings();
            ResolverConfig = RuntimeConfig.GetDeserializedConfig<ResolverConfig>(resolvers);
            RuntimeConfiguration.ConnectionString = connectionString;
            if (!string.IsNullOrEmpty(ResolverConfig.GraphQLSchema) && !string.IsNullOrEmpty(schema))
            {
                throw new Exception("The schema is set in two places, crashing to avoid issues.");
            }

            ResolverConfig = ResolverConfig with { GraphQLSchema = schema };

            EventHandler<RuntimeConfig>? handlers = RuntimeConfigLoaded;
            if (handlers != null)
            {
                handlers(this, RuntimeConfiguration);
            }
        }
    }
}
