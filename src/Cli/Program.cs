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

            // Logger setup and configuration
            ILoggerFactory loggerFactory = Utils.LoggerFactoryForCli;
            ILogger<Program> cliLogger = loggerFactory.CreateLogger<Program>();
            ILogger<ConfigGenerator> configGeneratorLogger = loggerFactory.CreateLogger<ConfigGenerator>();
            ILogger<Utils> cliUtilsLogger = loggerFactory.CreateLogger<Utils>();
            ConfigGenerator.SetLoggerForCliConfigGenerator(configGeneratorLogger);
            Utils.SetCliUtilsLogger(cliUtilsLogger);

            // Sets up the filesystem used for reading and writing runtime configuration files.
            IFileSystem fileSystem = new FileSystem();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);

            return Execute(args, cliLogger, fileSystem, loader);
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
            int result = parser.ParseArguments<InitOptions, AddOptions, UpdateOptions, StartOptions, ValidateOptions, ExportOptions, AddTelemetryOptions>(args)
                .MapResult(
                    (InitOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (AddOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (UpdateOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (StartOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (ValidateOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (AddTelemetryOptions options) => options.Handler(cliLogger, loader, fileSystem),
                    (ExportOptions options) => Exporter.Export(options, cliLogger, loader, fileSystem),
                    errors => DabCliParserErrorHandler.ProcessErrorsAndReturnExitCode(errors));

            return result;
        }
    }
}
