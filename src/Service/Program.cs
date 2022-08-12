using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (!StartEngine(args))
            {
                Environment.ExitCode = -1;
            }
        }

        public static bool StartEngine(string[] args)
        {
            Console.WriteLine("Starting the runtime engine...");
            try
            {
                CreateHostBuilder(args).Build().Run();
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unable to launch the runtime due to: {ex}");
                return false;
            }
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
                    ILoggerFactory? loggerFactory = LoggerFactory
                        .Create(builder =>
                        {
                            builder.AddConsole();
                        });
                    ILogger<Startup>? startupLogger = loggerFactory.CreateLogger<Startup>();
                    ILogger<RuntimeConfigProvider>? configProviderLogger = loggerFactory.CreateLogger<RuntimeConfigProvider>();
                    webBuilder.UseStartup(builder =>
                    {
                        return new Startup(builder.Configuration, startupLogger, configProviderLogger);
                    });
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
            .UseStartup<Startup>();

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
            string configFileName
                = RuntimeConfigPath.GetFileNameForEnvironment(env.EnvironmentName, considerOverrides: true);
            Dictionary<string, string> configFileNameMap = new()
            {
                {
                    nameof(RuntimeConfigPath.ConfigFileName),
                    configFileName
                }
            };

            configurationBuilder
                .AddInMemoryCollection(configFileNameMap)
                .AddEnvironmentVariables(prefix: RuntimeConfigPath.ENVIRONMENT_PREFIX)
                .AddCommandLine(args);
        }
    }
}
