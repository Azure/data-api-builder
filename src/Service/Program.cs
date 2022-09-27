using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
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
            // Unable to use ILogger because this code is invoked before LoggerFactory
            // is instantiated.
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
                            LogLevel logLevel = GetLogLevel(args);
                            // Category defines the namespace we will log from,
                            // including all sub-domains. ie: "Azure" includes
                            // "Azure.DataApiBuilder.Service"
                            builder.AddFilter(category: "Microsoft", logLevel);
                            builder.AddFilter(category: "Azure", logLevel);
                            builder.AddFilter(category: "Default", logLevel);
                            builder.AddConsole();
                        });
                    ILogger<Startup>? startupLogger = loggerFactory.CreateLogger<Startup>();
                    ILogger<RuntimeConfigProvider>? configProviderLogger = loggerFactory.CreateLogger<RuntimeConfigProvider>();
                    webBuilder.UseStartup(builder =>
                    {
                        return new Startup(builder.Configuration, startupLogger, configProviderLogger);
                    });
                });

        /// <summary>
        /// Iterate through args and based on values present
        /// set the appropriate log level. If --LogLevel is present
        /// the next value in args must be "0" through "6", or
        /// --LogLevel must be the last element in args. In any other
        /// case we throw an exception. If --LogLevel is the last element
        /// in args then we ignore it to maintain engine's behavior prior
        /// to this change.
        /// </summary>
        /// <param name="args">array that may contain log level information.</param>
        /// <returns>Appropriate log level.</returns>
        private static LogLevel GetLogLevel(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("--LogLevel"))
                {
                    if (args.Length <= i + 1)
                    {
                        break;
                    }

                    if (Enum.TryParse(args[i + 1], out LogLevel logLevel))
                    {
                        return logLevel;
                    }
                    else
                    {
                        throw new DataApiBuilderException(
                            message: $"LogLevel's valid range is 0 to 6, your value: {args[i]}",
                            statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                    }
                }
            }

            return LogLevel.Error;
        }

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
