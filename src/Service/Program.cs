// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Telemetry;
using Azure.DataApiBuilder.Service.Utilities;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Core;
using Serilog.Extensions.Logging;

namespace Azure.DataApiBuilder.Service
{
    public class Program
    {
        public static bool IsHttpsRedirectionDisabled { get; private set; }
        public static DynamicLogLevelProvider LogLevelProvider = new();

        public static void Main(string[] args)
        {
            bool runMcpStdio = McpStdioHelper.ShouldRunMcpStdio(args, out string? mcpRole);

            if (runMcpStdio)
            {
                Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }

            if (!ValidateAspNetCoreUrls())
            {
                Console.Error.WriteLine("Invalid ASPNETCORE_URLS format. e.g.: ASPNETCORE_URLS=\"http://localhost:5000;https://localhost:5001\"");
                Environment.ExitCode = -1;
                return;
            }

            if (!StartEngine(args, runMcpStdio, mcpRole))
            {
                Environment.ExitCode = -1;
            }
        }

        public static bool StartEngine(string[] args, bool runMcpStdio, string? mcpRole)
        {
            try
            {
                // Initialize log level EARLY, before building the host.
                // This ensures logging filters are effective during the entire host build process.
                // For MCP mode, we also read the config file early to check for log level override.
                LogLevel initialLogLevel = GetLogLevelFromCommandLineArgs(args, runMcpStdio, out bool isCliOverridden, out bool isConfigOverridden);

                LogLevelProvider.SetInitialLogLevel(initialLogLevel, isCliOverridden, isConfigOverridden);

                // For MCP stdio mode, redirect Console.Out to keep stdout clean for JSON-RPC.
                // MCP SDK uses Console.OpenStandardOutput() which gets the real stdout, unaffected by this redirect.
                if (runMcpStdio)
                {
                    // When LogLevel.None, redirect to null stream for ZERO output.
                    // Otherwise redirect to stderr so logs don't pollute JSON-RPC.
                    if (initialLogLevel == LogLevel.None)
                    {
                        Console.SetOut(TextWriter.Null);
                        Console.SetError(TextWriter.Null);
                    }
                    else
                    {
                        Console.SetOut(Console.Error);
                    }
                }

                IHost host = CreateHostBuilder(args, runMcpStdio, mcpRole).Build();

                if (runMcpStdio)
                {
                    return McpStdioHelper.RunMcpStdioHost(host);
                }

                // Normal web mode
                host.Run();
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

        // Compatibility overload used by external callers that do not pass the runMcpStdio flag.
        public static bool StartEngine(string[] args)
        {
            bool runMcpStdio = McpStdioHelper.ShouldRunMcpStdio(args, out string? mcpRole);
            return StartEngine(args, runMcpStdio, mcpRole: mcpRole);
        }

        public static IHostBuilder CreateHostBuilder(string[] args, bool runMcpStdio, string? mcpRole)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder =>
                {
                    AddConfigurationProviders(builder, args);
                    if (runMcpStdio)
                    {
                        McpStdioHelper.ConfigureMcpStdio(builder, mcpRole);
                    }
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(LogLevelProvider);
                    services.AddSingleton<ILogLevelController>(LogLevelProvider);
                })
                .ConfigureLogging(logging =>
                {
                    // For MCP stdio mode, we need dynamic log level control via logging/setLevel.
                    // Set framework minimum to Trace so all logs pass through to the dynamic filter.
                    // The dynamic AddFilter() will do the actual filtering based on current level.
                    // For non-MCP mode, use the configured level directly.
                    if (runMcpStdio)
                    {
                        // Allow all logs through framework, filter dynamically
                        logging.SetMinimumLevel(LogLevel.Trace);
                    }
                    else
                    {
                        logging.SetMinimumLevel(LogLevelProvider.CurrentLogLevel);
                    }

                    // Add filter for dynamic log level changes (e.g., via MCP logging/setLevel)
                    logging.AddFilter(logLevel => LogLevelProvider.ShouldLog(logLevel));
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    // LogLevelProvider was already initialized in StartEngine before CreateHostBuilder.
                    // Use the already-set values to avoid re-parsing args.
                    Startup.MinimumLogLevel = LogLevelProvider.CurrentLogLevel;
                    Startup.IsLogLevelOverriddenByCli = LogLevelProvider.IsCliOverridden;
                    ILoggerFactory loggerFactory = GetLoggerFactoryForLogLevel(Startup.MinimumLogLevel, stdio: runMcpStdio);
                    ILogger<Startup> startupLogger = loggerFactory.CreateLogger<Startup>();
                    DisableHttpsRedirectionIfNeeded(args);
                    webBuilder.UseStartup(builder => new Startup(builder.Configuration, startupLogger));
                });
        }

        /// <summary>
        /// Extracts the log level from the command line arguments and optionally from config.
        /// When --LogLevel is present, returns that value with CLI override flag set.
        /// When in MCP stdio mode without explicit --LogLevel, reads the config file to check for log level.
        /// When in normal mode without explicit --LogLevel, defaults to Error (UpdateFromRuntimeConfig()
        /// will later adjust based on config: Debug for Development mode, Error for Production mode).
        /// </summary>
        /// <param name="args">Array that may contain log level information.</param>
        /// <param name="runMcpStdio">Whether running in MCP stdio mode.</param>
        /// <param name="isLogLevelOverridenByCli">Sets if log level is found in the args from CLI.</param>
        /// <param name="isConfigOverridden">Sets if log level is found in the config file (MCP mode only).</param>
        /// <returns>Appropriate log level.</returns>
        private static LogLevel GetLogLevelFromCommandLineArgs(string[] args, bool runMcpStdio, out bool isLogLevelOverridenByCli, out bool isConfigOverridden)
        {
            Command cmd = new(name: "start");
            Option<LogLevel> logLevelOption = new(name: "--LogLevel");
            Option<string> configFileOption = new(name: "--ConfigFileName");
            cmd.AddOption(logLevelOption);
            cmd.AddOption(configFileOption);
            ParseResult result = GetParseResult(cmd, args);

            LogLevel logLevel;
            isConfigOverridden = false;

            // Check if --LogLevel was explicitly specified via CLI
            bool hasCliLogLevel = args.Any(a => string.Equals(a, "--LogLevel", StringComparison.OrdinalIgnoreCase));

            if (hasCliLogLevel)
            {
                // User explicitly set --LogLevel via CLI (highest priority)
                logLevel = result.GetValueForOption(logLevelOption);
                isLogLevelOverridenByCli = true;
            }
            else if (runMcpStdio)
            {
                // MCP stdio mode without explicit --LogLevel: check config for log level (second priority)
                isLogLevelOverridenByCli = false;
                logLevel = LogLevel.None; // Default if config doesn't have log level

                string? configFilePath = result.GetValueForOption(configFileOption);
                if (!string.IsNullOrWhiteSpace(configFilePath) && TryGetLogLevelFromConfig(configFilePath, out LogLevel configLogLevel))
                {
                    logLevel = configLogLevel;
                    isConfigOverridden = true;
                }
            }
            else
            {
                // Normal (non-MCP) mode without explicit --LogLevel:
                // Start with Error as fallback. UpdateFromRuntimeConfig() will later
                // adjust based on config: Debug for Development mode, Error for Production mode.
                // This initial value is used before config is loaded.
                logLevel = LogLevel.Error;
                isLogLevelOverridenByCli = false;
            }

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
        /// Attempts to read the config file early to extract the log level.
        /// This is used in MCP stdio mode to determine the Console redirect before host build.
        /// </summary>
        /// <param name="configFilePath">Path to the config file.</param>
        /// <param name="logLevel">The log level from config, if found.</param>
        /// <returns>True if config has an explicit log level; false otherwise.</returns>
        private static bool TryGetLogLevelFromConfig(string configFilePath, out LogLevel logLevel)
        {
            logLevel = LogLevel.None;
            try
            {
                if (!File.Exists(configFilePath))
                {
                    return false;
                }

                string configJson = File.ReadAllText(configFilePath);
                if (RuntimeConfigLoader.TryParseConfig(configJson, out RuntimeConfig? config) && config is not null)
                {
                    if (config.HasExplicitLogLevel())
                    {
                        // Use the config's method to get the resolved log level
                        logLevel = config.GetConfiguredLogLevel();
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore config parse errors - fall back to default log level
            }

            return false;
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
            Parser parser = new(cmdConfig);
            return parser.Parse(args);
        }

        /// <summary>
        /// Creates a LoggerFactory and add filter with the given LogLevel.
        /// </summary>
        /// <param name="logLevel">Minimum log level.</param>
        /// <param name="appTelemetryClient">Telemetry client</param>
        /// <param name="logLevelInitializer">Hot-reloadable log level</param>
        /// <param name="serilogLogger">Core Serilog logging pipeline</param>
        /// <param name="stdio">Whether the logger is for stdio mode</param>
        /// <returns>ILoggerFactory</returns>
        public static ILoggerFactory GetLoggerFactoryForLogLevel(
            LogLevel logLevel,
            TelemetryClient? appTelemetryClient = null,
            LogLevelInitializer? logLevelInitializer = null,
            Logger? serilogLogger = null,
            bool stdio = false)
        {
            return LoggerFactory
                .Create(builder =>
                {
                    // Category defines the namespace we will log from,
                    // including all subdomains. ie: "Azure" includes
                    // "Azure.DataApiBuilder.Service"
                    if (logLevelInitializer is null)
                    {
                        builder.AddFilter(category: "Microsoft", logLevel => LogLevelProvider.ShouldLog(logLevel));
                        builder.AddFilter(category: "Azure", logLevel => LogLevelProvider.ShouldLog(logLevel));
                        builder.AddFilter(category: "Default", logLevel => LogLevelProvider.ShouldLog(logLevel));
                    }
                    else
                    {
                        builder.AddFilter(category: "Microsoft", level => level >= logLevelInitializer.MinLogLevel);
                        builder.AddFilter(category: "Azure", level => level >= logLevelInitializer.MinLogLevel);
                        builder.AddFilter(category: "Default", level => level >= logLevelInitializer.MinLogLevel);
                    }

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

                    if (Startup.OpenTelemetryOptions.Enabled
                        && Uri.TryCreate(Startup.OpenTelemetryOptions.Endpoint, UriKind.Absolute, out Uri? otlpEndpoint))
                    {
                        builder.AddOpenTelemetry(logging =>
                        {
                            logging.IncludeFormattedMessage = true;
                            logging.IncludeScopes = true;
                            logging.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(Startup.OpenTelemetryOptions.ServiceName!));
                            logging.AddOtlpExporter(configure =>
                            {
                                configure.Endpoint = otlpEndpoint;
                                configure.Headers = Startup.OpenTelemetryOptions.Headers;
                                configure.Protocol = OtlpExportProtocol.Grpc;
                            });
                        });
                    }

                    if (Startup.IsAzureLogAnalyticsAvailable(Startup.AzureLogAnalyticsOptions))
                    {
                        builder.AddProvider(new AzureLogAnalyticsLoggerProvider(Startup.CustomLogCollector));

                        if (logLevelInitializer is null)
                        {
                            builder.AddFilter<AzureLogAnalyticsLoggerProvider>(category: string.Empty, logLevel);
                        }
                        else
                        {
                            builder.AddFilter<AzureLogAnalyticsLoggerProvider>(category: string.Empty, level => level >= logLevelInitializer.MinLogLevel);
                        }
                    }

                    if (Startup.FileSinkOptions.Enabled && serilogLogger is not null)
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

                    // In stdio mode, route console logs to STDERR to keep STDOUT clean for MCP JSON.
                    // Only add console logger if log level is not None (silent mode).
                    if (stdio)
                    {
                        builder.ClearProviders();

                        // Only add ConsoleLoggerProvider if we actually want logs.
                        // When LogLevel.None, skip the console logger entirely for true silence.
                        if (LogLevelProvider.CurrentLogLevel != LogLevel.None)
                        {
                            builder.AddConsole(options =>
                            {
                                options.LogToStandardErrorThreshold = LogLevel.Trace;
                            });
                        }
                    }
                    else
                    {
                        builder.AddConsole();
                    }
                });
        }

        /// <summary>
        /// Use CommandLine parser to check for the flag `--no-https-redirect`.
        /// If it is present, https redirection is disabled.
        /// By Default, it is enabled.
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
            WebHost
                .CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((_, builder) =>
                {
                    AddConfigurationProviders(builder, args);
                    DisableHttpsRedirectionIfNeeded(args);
                })
                .UseStartup<Startup>();

        // This is used for testing purposes only. The test web server takes in a
        // IWebHostBuilder, instead of a IHostBuilder.
        public static IWebHostBuilder CreateWebHostFromInMemoryUpdatableConfBuilder(string[] args) =>
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
}
