// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace Azure.DataApiBuilder.Service
{
    public class Program
    {
        public static bool IsHttpsRedirectionDisabled { get; private set; }

        public static void Main(string[] args)
        {
            if (!ValidateAspNetCoreUrls())
            {
                Console.Error.WriteLine("Invalid ASPNETCORE_URLS format. e.g.: ASPNETCORE_URLS=\"http://localhost:5000;https://localhost:5001\"");
                Environment.ExitCode = -1;
                return;
            }

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
            // Catch exception raised by explicit call to IHostApplicationLifetime.StopApplication()
            catch (TaskCanceledException)
            {
                // Do not log the exception here because exceptions raised during startup
                // are already automatically written to the console.
                Console.Error.WriteLine("Unable to launch the Data API builder engine.");
                return false;
            }
            // Catch all remaining unhandled exceptions which may be due to server host operation.
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unable to launch the runtime due to: {ex}");
                return false;
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder =>
                {
                    AddConfigurationProviders(builder, args);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    Startup.MinimumLogLevel = GetLogLevelFromCommandLineArgs(args, out Startup.IsLogLevelOverriddenByCli);
                    ILoggerFactory? loggerFactory = GetLoggerFactoryForLogLevel(Startup.MinimumLogLevel);
                    ILogger<Startup>? startupLogger = loggerFactory.CreateLogger<Startup>();
                    DisableHttpsRedirectionIfNeeded(args);
                    webBuilder.UseStartup(builder => new Startup(builder.Configuration, startupLogger));
                });
        }

        /// <summary>
        /// Using System.CommandLine Parser to parse args and return
        /// the correct log level. We save if there is a log level in args through
        /// the out param. For log level out of range we throw an exception.
        /// </summary>
        /// <param name="args">array that may contain log level information.</param>
        /// <param name="isLogLevelOverridenByCli">sets if log level is found in the args.</param>
        /// <returns>Appropriate log level.</returns>
        private static LogLevel GetLogLevelFromCommandLineArgs(string[] args, out bool isLogLevelOverridenByCli)
        {
            Command cmd = new(name: "start");
            Option<LogLevel> logLevelOption = new(name: "--LogLevel");
            cmd.AddOption(logLevelOption);
            ParseResult result = GetParseResult(cmd, args);
            bool matchedToken = result.Tokens.Count - result.UnmatchedTokens.Count - result.UnparsedTokens.Count > 1;
            LogLevel logLevel = matchedToken ? result.GetValueForOption<LogLevel>(logLevelOption) : LogLevel.Error;
            isLogLevelOverridenByCli = matchedToken ? true : false;

            if (logLevel is > LogLevel.None or < LogLevel.Trace)
            {
                throw new DataApiBuilderException(
                    message: $"LogLevel's valid range is 0 to 6, your value: {logLevel}, see: " +
                    $"https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.loglevel",
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            return logLevel;
        }

        /// <summary>
        /// Helper function returns ParseResult for a given command and
        /// arguments.
        /// </summary>
        /// <param name="cmd">The command.</param>
        /// <param name="args">The arguments.</param>
        /// <returns>ParsedResult</returns>
        private static ParseResult GetParseResult(Command cmd, string[] args)
        {
            CommandLineConfiguration cmdConfig = new(cmd);
            System.CommandLine.Parsing.Parser parser = new(cmdConfig);
            return parser.Parse(args);
        }

        /// <summary>
        /// Creates a LoggerFactory and add filter with the given LogLevel.
        /// </summary>
        /// <param name="logLevel">minimum log level.</param>
        /// <param name="appTelemetryClient">Telemetry client</param>
        public static ILoggerFactory GetLoggerFactoryForLogLevel(LogLevel logLevel, TelemetryClient? appTelemetryClient = null)
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

                    // For Sending all the ILogger logs to Application Insights
                    if (Startup.AppInsightsOptions.Enabled && !string.IsNullOrWhiteSpace(Startup.AppInsightsOptions.ConnectionString))
                    {
                        builder.AddApplicationInsights(configureTelemetryConfiguration: (config) =>
                            {
                                config.ConnectionString = Startup.AppInsightsOptions.ConnectionString;
                                if (Startup.CustomTelemetryChannel is not null)
                                {
                                    config.TelemetryChannel = Startup.CustomTelemetryChannel;
                                }
                            },
                            configureApplicationInsightsLoggerOptions: (options) => { }
                        )
                        .AddFilter<ApplicationInsightsLoggerProvider>(category: string.Empty, logLevel);
                    }

                    if (Startup.OpenTelemetryOptions.Enabled && !string.IsNullOrWhiteSpace(Startup.OpenTelemetryOptions.Endpoint))
                    {
                        builder.AddOpenTelemetry(logging =>
                        {
                            logging.IncludeFormattedMessage = true;
                            logging.IncludeScopes = true;
                            logging.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(Startup.OpenTelemetryOptions.ServiceName!));
                            logging.AddOtlpExporter(configure =>
                            {
                                configure.Endpoint = new Uri(Startup.OpenTelemetryOptions.Endpoint);
                                configure.Headers = Startup.OpenTelemetryOptions.Headers;
                                configure.Protocol = OtlpExportProtocol.Grpc;
                            });
                        });
                    }

                    builder.AddConsole();
                });
        }

        /// <summary>
        /// Use CommandLine parser to check for the flag `--no-https-redirect`.
        /// If it is present, https redirection is disabled.
        /// By Default it is enabled.
        /// </summary>
        /// <param name="args">array that may contain flag to disable https redirection.</param>
        private static void DisableHttpsRedirectionIfNeeded(string[] args)
        {
            Command cmd = new(name: "start");
            Option<string> httpsRedirectFlagOption = new(name: Startup.NO_HTTPS_REDIRECT_FLAG);
            cmd.AddOption(httpsRedirectFlagOption);
            ParseResult result = GetParseResult(cmd, args);
            if (result.Tokens.Count - result.UnmatchedTokens.Count - result.UnparsedTokens.Count > 0)
            {
                Console.WriteLine("Redirecting to https is disabled.");
                IsHttpsRedirectionDisabled = true;
                return;
            }

            IsHttpsRedirectionDisabled = false;
        }

        // This is used for testing purposes only. The test web server takes in a
        // IWebHostBuilder, instead of a IHostBuilder.
        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, builder) =>
            {
                AddConfigurationProviders(builder, args);
                DisableHttpsRedirectionIfNeeded(args);
            })
            .UseStartup<Startup>();

        // This is used for testing purposes only. The test web server takes in a
        // IWebHostBuilder, instead of a IHostBuilder.
        public static IWebHostBuilder CreateWebHostFromInMemoryUpdateableConfBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
            .UseStartup<Startup>();

        /// <summary>
        /// Adds the various configuration providers.
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder.</param>
        /// <param name="args">The command line arguments.</param>
        private static void AddConfigurationProviders(
            IConfigurationBuilder configurationBuilder,
            string[] args)
        {
            configurationBuilder
                .AddEnvironmentVariables(prefix: FileSystemRuntimeConfigLoader.ENVIRONMENT_PREFIX)
                .AddCommandLine(args);
        }

        /// <summary>
        /// Validates the URLs specified in the ASPNETCORE_URLS environment variable.
        /// Ensures that each URL is valid and properly formatted.
        /// </summary>
        internal static bool ValidateAspNetCoreUrls()
        {
            if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is not string urls)
            {
                return true; // If the environment variable is missing, then it cannot be invalid.
            }

            if (string.IsNullOrWhiteSpace(urls))
            {
                return false;
            }

            char[] separators = new[] { ';', ',', ' ' };
            string[] urlList = urls.Split(separators, StringSplitOptions.RemoveEmptyEntries);

            foreach (string url in urlList)
            {
                if (IsUnixDomainSocketUrl(url))
                {
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || !ValidateUnixDomainSocketUrl(url))
                    {
                        return false;
                    }

                    continue;
                }

                string testUrl = ReplaceWildcardHost(url);
                if (!Uri.TryCreate(testUrl, UriKind.Absolute, out Uri? uriResult) ||
                    (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
                {
                    return false;
                }
            }

            return true;

            static bool IsUnixDomainSocketUrl(string url) =>
                Regex.IsMatch(url, @"^https?://unix:", RegexOptions.IgnoreCase);

            static bool ValidateUnixDomainSocketUrl(string url) =>
                Regex.IsMatch(url, @"^https?://unix:/\S+");

            static string ReplaceWildcardHost(string url) =>
                Regex.Replace(url, @"^(https?://)[\+\*]", "$1localhost", RegexOptions.IgnoreCase);
        }
    }
}
