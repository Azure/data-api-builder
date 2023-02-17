// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
                    Startup.MinimumLogLevel = GetLogLevelFromCommandLineArgs(args, out Startup.IsLogLevelOverriddenByCli);
                    ILoggerFactory? loggerFactory = GetLoggerFactoryForLogLevel(Startup.MinimumLogLevel);
                    ILogger<Startup>? startupLogger = loggerFactory.CreateLogger<Startup>();
                    ILogger<RuntimeConfigProvider>? configProviderLogger = loggerFactory.CreateLogger<RuntimeConfigProvider>();
                    DisableHttpsRedirectionIfNeeded(args);
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
        /// <param name="isLogLevelOverridenByCli">sets if log level is found in the args.</param>
        /// <returns>Appropriate log level.</returns>
        private static LogLevel GetLogLevelFromCommandLineArgs(string[] args, out bool isLogLevelOverridenByCli)
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
                        isLogLevelOverridenByCli = true;
                        return logLevel;
                    }
                    else
                    {
                        throw new DataApiBuilderException(
                            message: $"LogLevel's valid range is 0 to 6, your value: {args[i]}, see: " +
                            $"https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.loglevel?view=dotnet-plat-ext-7.0",
                            statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                    }
                }
            }

            isLogLevelOverridenByCli = false;
            return LogLevel.Error;
        }

        /// <summary>
        /// Creates a LoggerFactory and add filter with the given LogLevel.
        /// </summary>
        /// <param name="logLevel">minimum log level.</param>
        public static ILoggerFactory GetLoggerFactoryForLogLevel(LogLevel logLevel)
        {
            return LoggerFactory
                .Create(builder =>
                {
                    // Category defines the namespace we will log from,
                    // including all sub-domains. ie: "Azure" includes
                    // "Azure.DataApiBuilder.Service"
                    builder.AddFilter(category: "Microsoft", logLevel);
                    builder.AddFilter(category: "Azure", logLevel);
                    builder.AddFilter(category: "Default", logLevel);
                    builder.AddConsole();
                });
        }

        /// <summary>
        /// Iterate through args from cli and check for the flag `--no-https-redirect`.
        /// If it is present, https redirection is disabled.
        /// By Default it is enabled.
        /// </summary>
        /// <param name="args">array that may contain flag to disable https redirection.</param>
        private static void DisableHttpsRedirectionIfNeeded(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals(Startup.NO_HTTPS_REDIRECT_FLAG))
                {
                    Console.WriteLine("Redirecting to https is disabled.");
                    RuntimeConfigProvider.IsHttpsRedirectionDisabled = true;
                    return;
                }
            }

            RuntimeConfigProvider.IsHttpsRedirectionDisabled = false;
        }

        // This is used for testing purposes only. The test web server takes in a
        // IWebHostbuilder, instead of a IHostBuilder.
        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, builder) =>
            {
                IHostEnvironment env = hostingContext.HostingEnvironment;
                AddConfigurationProviders(env, builder, args);
                DisableHttpsRedirectionIfNeeded(args);
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
