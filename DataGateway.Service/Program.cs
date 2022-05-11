using System.Collections.Generic;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Azure.DataGateway.Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, configurationBuilder) =>
                {
                    IHostEnvironment env = hostingContext.HostingEnvironment;
                    AddConfigurationProviders(env, configurationBuilder, args);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });

        // This is used for testing purposes only. The test web server takes in a
        // IWebHostbuilder, instead of a IHostBuilder.
        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, builder) =>
            {
                IHostEnvironment env = hostingContext.HostingEnvironment;
                AddConfigurationProviders(env, builder, args);
            }).UseStartup<Startup>();

        // This is used for testing purposes only. The test web server takes in a
        // IWebHostbuilder, instead of a IHostBuilder.
        public static IWebHostBuilder CreateWebHostFromInMemoryUpdateableConfBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((builder) =>
            {
                builder.AddInMemoryUpdateableConfiguration();
            }).UseStartup<Startup>();

        /// <summary>
        /// Adds the various configuration providers.
        /// </summary>
        /// <param name="env">The hosting environment.</param>
        /// <param name="configurationBuilder">The configuration builder.</param>
        /// <param name="args">The command line arguments.</param>
        private static void AddConfigurationProviders(
            IHostEnvironment env,
            IConfigurationBuilder configurationBuilder,
            string[] args)
        {
            configurationBuilder.Sources.Clear();

            string configFileName
                = RuntimeConfigPath.GetFileNameForEnvironment(env.EnvironmentName);
            Dictionary<string, string> configFileNameMap = new()
            {
                {
                    nameof(RuntimeConfigPath.ConfigFileName),
                    configFileName
                }
            };

            configurationBuilder
                .AddInMemoryCollection(configFileNameMap);

            configurationBuilder.AddCommandLine(args);

            configurationBuilder.AddInMemoryUpdateableConfiguration();
        }
    }
}
