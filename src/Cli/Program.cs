// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Cli.Commands;
using CommandLine;
using Microsoft.Extensions.Logging;

namespace Cli
{
    /// <summary>
    /// Main class for CLI
    /// </summary>
    public class Program
    {
        public const string PRODUCT_NAME = "Microsoft.DataApiBuilder";

        /// <summary>
        /// Main CLI entry point
        /// </summary>
        /// <param name="args">CLI arguments</param>
        /// <returns>0 on success, -1 on failure.</returns>
        public static int Main(string[] args)
        {
            // Load environment variables from .env file if present.
            DotNetEnv.Env.Load();

            // Pre-parse the --LogLevel option so the CLI logger respects the
            // requested log level before the engine starts.
            LogLevel cliLogLevel = PreParseLogLevel(args);
            Utils.LoggerFactoryForCli = Utils.GetLoggerFactoryForCli(cliLogLevel);

            // Logger setup and configuration
            ILoggerFactory loggerFactory = Utils.LoggerFactoryForCli;
            ILogger<Program> cliLogger = loggerFactory.CreateLogger<Program>();
            ILogger<ConfigGenerator> configGeneratorLogger = loggerFactory.CreateLogger<ConfigGenerator>();
            ILogger<Utils> cliUtilsLogger = loggerFactory.CreateLogger<Utils>();
            ConfigGenerator.SetLoggerForCliConfigGenerator(configGeneratorLogger);
            Utils.SetCliUtilsLogger(cliUtilsLogger);

            // Sets up the filesystem used for reading and writing runtime configuration files.
            IFileSystem fileSystem = new FileSystem();
            FileSystemRuntimeConfigLoader loader = new(fileSystem, handler: null, isCliLoader: true);

            return Execute(args, cliLogger, fileSystem, loader);
        }

        /// <summary>
        /// Pre-parses the --LogLevel option from the command-line arguments before full
        /// argument parsing, so the CLI logger can be configured at the right minimum
        /// level for CLI phase messages (version info, config loading, etc.).
        /// </summary>
        /// <param name="args">Command line arguments</param>
        /// <returns>The parsed LogLevel, or Information if not specified or invalid.</returns>
        internal static LogLevel PreParseLogLevel(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                // Handle --LogLevel None (two separate tokens)
                if (args[i].Equals("--LogLevel", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (Enum.TryParse(args[i + 1], ignoreCase: true, out LogLevel level))
                    {
                        return level;
                    }
                }
                // Handle --LogLevel=None (single token with equals sign)
                else if (args[i].StartsWith("--LogLevel=", StringComparison.OrdinalIgnoreCase))
                {
                    string value = args[i]["--LogLevel=".Length..];
                    if (Enum.TryParse(value, ignoreCase: true, out LogLevel level))
                    {
                        return level;
                    }
                }
            }

            return LogLevel.Information;
        }

        /// <summary>
        /// Execute the CLI command
        /// </summary>
        /// <param name="args">Command line arguments</param>
        /// <param name="cliLogger">Logger used as sink for informational and error messages.</param>
        /// <param name="fileSystem">Filesystem used for reading and writing configuration files, and exporting GraphQL schemas.</param>
        /// <param name="loader">Loads the runtime config.</param>
        /// <returns>Exit Code: 0 success, -1 failure</returns>
        public static int Execute(string[] args, ILogger cliLogger, IFileSystem fileSystem, FileSystemRuntimeConfigLoader loader)
        {
            Parser parser = new(settings =>
            {
                settings.CaseInsensitiveEnumValues = true;
                settings.HelpWriter = Console.Out;
            });

            // Parsing user arguments and executing required methods.
            int result = parser.ParseArguments<InitOptions, AddOptions, UpdateOptions, StartOptions, ValidateOptions, ExportOptions, AddTelemetryOptions, ConfigureOptions, AutoConfigOptions>(args)
                .MapResult(
                    (InitOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (AddOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (UpdateOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (StartOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (ValidateOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (AddTelemetryOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (ConfigureOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (AutoConfigOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (ExportOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    errors => DabCliParserErrorHandler.ProcessErrorsAndReturnExitCode(errors));

            return result;
        }
    }
}
