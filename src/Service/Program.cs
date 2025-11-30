// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.CommandLine;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Core;
using Serilog.Extensions.Logging;

// Program class for methods and entry point
public partial class Program
{
    public static bool IsHttpsRedirectionDisabled { get; private set; }

    /// <summary>
    /// Application entry point.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Exit code: 0 for success, -1 for failure.</returns>
    public static int Main(string[] args)
    {
        if (!ValidateAspNetCoreUrls())
        {
            Console.Error.WriteLine("Invalid ASPNETCORE_URLS format. e.g.: ASPNETCORE_URLS=\"http://localhost:5000;https://localhost:5001\"");
            Environment.ExitCode = -1;
            return -1;
        }

        if (!StartEngine(args))
        {
            Environment.ExitCode = -1;
            return -1;
        }

        return 0;
    }

    public static bool StartEngine(string[] args)
    {
        // Unable to use ILogger because this code is invoked before LoggerFactory
        // is instantiated.
        Console.WriteLine("Starting the runtime engine...");
        try
        {
            WebApplicationBuilder builder = CreateWebApplicationBuilder(args);
            WebApplication app = builder.Build();
            app.ConfigurePipeline();
            app.Run();
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

    public static WebApplicationBuilder CreateWebApplicationBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        AddConfigurationProviders(builder.Configuration, args);
        StartupConfiguration.MinimumLogLevel = GetLogLevelFromCommandLineArgs(args, out StartupConfiguration.IsLogLevelOverriddenByCli);
        DisableHttpsRedirectionIfNeeded(args);
        
        builder.ConfigureServices();
        
        return builder;
    }

    // Backward compatibility: Keep CreateHostBuilder for tests
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(builder =>
            {
                AddConfigurationProviders(builder, args);
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                StartupConfiguration.MinimumLogLevel = GetLogLevelFromCommandLineArgs(args, out StartupConfiguration.IsLogLevelOverriddenByCli);
                ILoggerFactory loggerFactory = GetLoggerFactoryForLogLevel(StartupConfiguration.MinimumLogLevel);
                #pragma warning disable CS0618 // Type or member is obsolete
                ILogger<Startup> startupLogger = loggerFactory.CreateLogger<Startup>();
                DisableHttpsRedirectionIfNeeded(args);
                webBuilder.UseStartup(builder => new Startup(builder.Configuration, startupLogger));
                #pragma warning restore CS0618
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
    internal static LogLevel GetLogLevelFromCommandLineArgs(string[] args, out bool isLogLevelOverridenByCli)
        {
            Option<LogLevel> logLevelOption = new(name: "--LogLevel");
            Command cmd = new(name: "start");
            cmd.Add(logLevelOption);
            ParseResult result = cmd.Parse(args);
            
            // Check if the option was explicitly provided by checking tokens
            bool matchedToken = result.Tokens.Any(t => t.Value == "--LogLevel");
            LogLevel logLevel = LogLevel.Error;
            
            if (matchedToken)
            {
                // Find the token after --LogLevel
                var tokens = result.Tokens.ToList();
                int logLevelIndex = tokens.FindIndex(t => t.Value == "--LogLevel");
                if (logLevelIndex >= 0 && logLevelIndex + 1 < tokens.Count)
                {
                    if (Enum.TryParse<LogLevel>(tokens[logLevelIndex + 1].Value, out var parsedLevel))
                    {
                        logLevel = parsedLevel;
                    }
                }
            }
            
            isLogLevelOverridenByCli = matchedToken;

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
    /// Creates a LoggerFactory and add filter with the given LogLevel.
    /// </summary>
    /// <param name="logLevel">Minimum log level.</param>
    /// <param name="appTelemetryClient">Telemetry client</param>
    /// <param name="logLevelInitializer">Hot-reloadable log level</param>
    /// <param name="serilogLogger">Core Serilog logging pipeline</param>
    public static ILoggerFactory GetLoggerFactoryForLogLevel(LogLevel logLevel, TelemetryClient? appTelemetryClient = null, LogLevelInitializer? logLevelInitializer = null, Logger? serilogLogger = null)
        {
            return LoggerFactory
                .Create(builder =>
                {
                    // Category defines the namespace we will log from,
                    // including all subdomains. ie: "Azure" includes
                    // "Azure.DataApiBuilder.Service"
                    if (logLevelInitializer is null)
                    {
                        builder.AddFilter(category: "Microsoft", logLevel);
                        builder.AddFilter(category: "Azure", logLevel);
                        builder.AddFilter(category: "Default", logLevel);
                    }
                    else
                    {
                        builder.AddFilter(category: "Microsoft", level => level >= logLevelInitializer.MinLogLevel);
                        builder.AddFilter(category: "Azure", level => level >= logLevelInitializer.MinLogLevel);
                        builder.AddFilter(category: "Default", level => level >= logLevelInitializer.MinLogLevel);
                    }

                    // For Sending all the ILogger logs to Application Insights
                    if (StartupConfiguration.AppInsightsOptions.Enabled && !string.IsNullOrWhiteSpace(StartupConfiguration.AppInsightsOptions.ConnectionString))
                    {
                        builder.AddApplicationInsights(configureTelemetryConfiguration: (config) =>
                            {
                                config.ConnectionString = StartupConfiguration.AppInsightsOptions.ConnectionString;
                                if (StartupConfiguration.CustomTelemetryChannel is not null)
                                {
                                    config.TelemetryChannel = StartupConfiguration.CustomTelemetryChannel;
                                }
                            },
                            configureApplicationInsightsLoggerOptions: _ => { }
                        );

                        if (logLevelInitializer is null)
                        {
                            builder.AddFilter<ApplicationInsightsLoggerProvider>(category: string.Empty, logLevel);
                        }
                        else
                        {
                            builder.AddFilter<ApplicationInsightsLoggerProvider>(category: string.Empty, level => level >= logLevelInitializer.MinLogLevel);
                        }
                    }

                    if (StartupConfiguration.OpenTelemetryOptions.Enabled && !string.IsNullOrWhiteSpace(StartupConfiguration.OpenTelemetryOptions.Endpoint))
                    {
                        builder.AddOpenTelemetry(logging =>
                        {
                            logging.IncludeFormattedMessage = true;
                            logging.IncludeScopes = true;
                            logging.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(StartupConfiguration.OpenTelemetryOptions.ServiceName!));
                            logging.AddOtlpExporter(configure =>
                            {
                                configure.Endpoint = new Uri(StartupConfiguration.OpenTelemetryOptions.Endpoint);
                                configure.Headers = StartupConfiguration.OpenTelemetryOptions.Headers;
                                configure.Protocol = OtlpExportProtocol.Grpc;
                            });
                        });
                    }

                    if (StartupConfiguration.IsAzureLogAnalyticsAvailable(StartupConfiguration.AzureLogAnalyticsOptions))
                    {
                        builder.AddProvider(new AzureLogAnalyticsLoggerProvider(StartupConfiguration.CustomLogCollector));

                        if (logLevelInitializer is null)
                        {
                            builder.AddFilter<AzureLogAnalyticsLoggerProvider>(category: string.Empty, logLevel);
                        }
                        else
                        {
                            builder.AddFilter<AzureLogAnalyticsLoggerProvider>(category: string.Empty, level => level >= logLevelInitializer.MinLogLevel);
                        }
                    }

                    if (StartupConfiguration.FileSinkOptions.Enabled && serilogLogger is not null)
                    {
                        builder.AddSerilog(serilogLogger);

                        if (logLevelInitializer is null)
                        {
                            builder.AddFilter<SerilogLoggerProvider>(category: string.Empty, logLevel);
                        }
                        else
                        {
                            builder.AddFilter<SerilogLoggerProvider>(category: string.Empty, level => level >= logLevelInitializer.MinLogLevel);
                        }
                    }

                    builder.AddConsole();
                });
        }

    /// <summary>
    /// Use CommandLine parser to check for the flag `--no-https-redirect`.
    /// If it is present, https redirection is disabled.
    /// By Default, it is enabled.
    /// </summary>
    /// <param name="args">array that may contain flag to disable https redirection.</param>
    internal static void DisableHttpsRedirectionIfNeeded(string[] args)
    {
        Command cmd = new(name: "start");
        Option<string> httpsRedirectFlagOption = new(name: StartupConfiguration.NO_HTTPS_REDIRECT_FLAG);
            cmd.Add(httpsRedirectFlagOption);
            ParseResult result = cmd.Parse(args);
            if (result.Tokens.Count - result.UnmatchedTokens.Count > 0)
            {
                Console.WriteLine("Redirecting to https is disabled.");
                IsHttpsRedirectionDisabled = true;
                return;
            }

            IsHttpsRedirectionDisabled = false;
        }

    // This is used for testing purposes only. The test web server takes in a
    // IWebHostBuilder, instead of a IHostBuilder.
#pragma warning disable ASPDEPR008
    public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
        WebHost
            .CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, builder) =>
            {
                AddConfigurationProviders(builder, args);
                DisableHttpsRedirectionIfNeeded(args);
            })
            #pragma warning disable CS0618 // Type or member is obsolete
            .UseStartup<Startup>();
            #pragma warning restore CS0618

    // This is used for testing purposes only. The test web server takes in a
    // IWebHostBuilder, instead of a IHostBuilder.
    public static IWebHostBuilder CreateWebHostFromInMemoryUpdatableConfBuilder(string[] args) =>
        WebHost.CreateDefaultBuilder(args)
        #pragma warning disable CS0618 // Type or member is obsolete
        .UseStartup<Startup>();
        #pragma warning restore CS0618
#pragma warning restore ASPDEPR008

    /// <summary>
    /// Adds the various configuration providers.
    /// </summary>
    /// <param name="configurationBuilder">The configuration builder.</param>
    /// <param name="args">The command line arguments.</param>
    internal static void AddConfigurationProviders(
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
            if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is not { } urls)
            {
                return true; // If the environment variable is missing, then it cannot be invalid.
            }

            if (string.IsNullOrWhiteSpace(urls))
            {
                return false;
            }

            char[] separators = [';', ',', ' '];
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
                if (!CheckSanityOfUrl(testUrl))
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

    public static bool CheckSanityOfUrl(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsedUri))
        {
            return false;
        }

        // Only allow HTTP or HTTPS schemes
        if (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        // Disallow empty hostnames
        if (string.IsNullOrWhiteSpace(parsedUri.Host))
        {
            return false;
        }

        return true;
    }
}
