// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.Extensions.DependencyInjection;
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
using System.Collections.Generic;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Service
{
    public class Program
    {
        public static bool IsHttpsRedirectionDisabled { get; private set; }

        public static void Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);    

            // Detect stdio mode as early as possible and route any Console.WriteLine to STDERR
            bool runMcpStdio = Array.Exists(args, a => string.Equals(a, "--mcp-stdio", StringComparison.OrdinalIgnoreCase));
            if (runMcpStdio)
            {
                // MCP requires STDOUT to contain only protocol JSON; send all other text to STDERR
                Console.SetOut(Console.Error);

                // Hint to logging pipeline (used below) to log to STDERR only
                Environment.SetEnvironmentVariable("DAB_MCP_STDIO", "1");

                // If caller provided an optional role token like `role:authenticated`, capture it and
                // force the runtime to use the Simulator authentication provider for this session.
                // This makes it easy to run MCP stdio sessions with a preconfigured permissions role.
                string? roleArg = Array.Find(args, a => a != null && a.StartsWith("role:", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(roleArg))
                {
                    string roleValue = roleArg.Substring(roleArg.IndexOf(':') + 1);
                    if (!string.IsNullOrWhiteSpace(roleValue))
                    {
                        Environment.SetEnvironmentVariable("DAB_MCP_STDIO_ROLE", roleValue);
                        Environment.SetEnvironmentVariable("DAB_MCP_SIMULATOR_AUTH", "1");
                    }
                }
            }

            if (!ValidateAspNetCoreUrls())
            {
                Console.Error.WriteLine("Invalid ASPNETCORE_URLS format. e.g.: ASPNETCORE_URLS=\"http://localhost:5000;https://localhost:5001\"");
                Environment.ExitCode = -1;
                return;
            }

            if (!StartEngine(args, runMcpStdio))
            {
                Environment.ExitCode = -1;
            }
        }

        public static bool StartEngine(string[] args, bool runMcpStdio)
        {
            Console.WriteLine("Starting the runtime engine...");
            try
            {
                IHost host = CreateHostBuilder(args).Build();

                if (runMcpStdio)
                {
                    // Start DI/telemetry/metadata etc., but don't block on Kestrel
                    host.Start();

                    Mcp.Core.McpToolRegistry registry = host.Services.GetRequiredService<Mcp.Core.McpToolRegistry>();
                    IEnumerable<Mcp.Model.IMcpTool> tools = host.Services.GetServices<Mcp.Model.IMcpTool>();
                    foreach (Mcp.Model.IMcpTool tool in tools)
                    {
                        Tool metadata = tool.GetToolMetadata();
                        Console.Error.WriteLine($"[MCP DEBUG] Registering tool: {metadata.Name}");
                        registry.RegisterTool(tool);
                    }

                    // Resolve and run the MCP stdio server from DI
                    IServiceScopeFactory scopeFactory = host.Services.GetRequiredService<IServiceScopeFactory>();
                    using IServiceScope scope = scopeFactory.CreateScope();
                    IHostApplicationLifetime lifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
                    Mcp.Core.IMcpStdioServer stdio = scope.ServiceProvider.GetRequiredService<Mcp.Core.IMcpStdioServer>();
                    
                    // Run the stdio loop until cancellation (Ctrl+C / process end)
                    stdio.RunAsync(lifetime.ApplicationStopping).GetAwaiter().GetResult();

                    host.StopAsync().GetAwaiter().GetResult();
                    return true;
                }

                // Normal web mode
                host.Run();
                return true;
            }
            catch (TaskCanceledException)
            {
                Console.Error.WriteLine("Unable to launch the Data API builder engine.");
                return false;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unable to launch the runtime due to: {ex}");
                return false;
            }
        }

        // Compatibility overload used by external callers that do not pass the runMcpStdio flag.
        public static bool StartEngine(string[] args)
        {
            bool runMcpStdio = Array.Exists(args, a => string.Equals(a, "--mcp-stdio", StringComparison.OrdinalIgnoreCase));
            return StartEngine(args, runMcpStdio);
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
                    ILoggerFactory loggerFactory = GetLoggerFactoryForLogLevel(Startup.MinimumLogLevel);
                    ILogger<Startup> startupLogger = loggerFactory.CreateLogger<Startup>();
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
            LogLevel logLevel = matchedToken ? result.GetValueForOption(logLevelOption) : LogLevel.Error;
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
        public static ILoggerFactory GetLoggerFactoryForLogLevel(LogLevel logLevel, TelemetryClient? appTelemetryClient = null, LogLevelInitializer? logLevelInitializer = null, Logger? serilogLogger = null)
        {
            return LoggerFactory
                .Create(builder =>
                {
                    bool stdio = string.Equals(Environment.GetEnvironmentVariable("DAB_MCP_STDIO"), "1", StringComparison.OrdinalIgnoreCase);

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

                    // In stdio mode, route console logs to STDERR to keep STDOUT clean for MCP JSON
                    if (stdio)
                    {
                        builder.ClearProviders();
                        builder.AddConsole(options =>
                        {
                            options.LogToStandardErrorThreshold = LogLevel.Trace;
                        });
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
