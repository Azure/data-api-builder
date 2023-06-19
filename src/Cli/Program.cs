// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CommandLine;
using Microsoft.Extensions.Logging;
using static Azure.DataApiBuilder.Service.Utils;
using static Cli.Utils;

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
            Parser parser = new(settings =>
                {
                    settings.CaseInsensitiveEnumValues = true;
                    settings.HelpWriter = Console.Out;
                }
            );

            // Load environment variables from .env file if present.
            DotNetEnv.Env.Load();

            // Setting up Logger for CLI.
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new CustomLoggerProvider());

            ILogger<Program> cliLogger = loggerFactory.CreateLogger<Program>();
            ILogger<ConfigGenerator> configGeneratorLogger = loggerFactory.CreateLogger<ConfigGenerator>();
            ILogger<Utils> cliUtilsLogger = loggerFactory.CreateLogger<Utils>();
            ConfigGenerator.SetLoggerForCliConfigGenerator(configGeneratorLogger);
            Utils.SetCliUtilsLogger(cliUtilsLogger);

            // To know if `--help` or `--version` was requested.
            bool isHelpOrVersionRequested = false;

            // Parsing user arguments and executing required methods.
            ParserResult<object>? result = parser.ParseArguments<InitOptions, AddOptions, UpdateOptions, StartOptions, ExportOptions>(args)
                .WithParsed((Action<InitOptions>)(options =>
                {
                    cliLogger.LogInformation($"{PRODUCT_NAME} {GetProductVersion()}");
                    bool isSuccess = ConfigGenerator.TryGenerateConfig(options);
                    if (isSuccess)
                    {
                        cliLogger.LogInformation($"Config file generated.");
                        cliLogger.LogInformation($"SUGGESTION: Use 'dab add [entity-name] [options]' to add new entities in your config.");
                    }
                    else
                    {
                        cliLogger.LogError($"Could not generate config file.");
                    }
                }))
                .WithParsed((Action<AddOptions>)(options =>
                {
                    cliLogger.LogInformation($"{PRODUCT_NAME} {GetProductVersion()}");
                    if (!IsEntityProvided(options.Entity, cliLogger, command: "add"))
                    {
                        return;
                    }

                    bool isSuccess = ConfigGenerator.TryAddEntityToConfigWithOptions(options);
                    if (isSuccess)
                    {
                        cliLogger.LogInformation($"Added new entity: {options.Entity} with source: {options.Source}" +
                            $" and permissions: {string.Join(SEPARATOR, options.Permissions.ToArray())}.");
                        cliLogger.LogInformation($"SUGGESTION: Use 'dab update [entity-name] [options]' to update any entities in your config.");
                    }
                    else
                    {
                        cliLogger.LogError($"Could not add entity: {options.Entity} with source: {options.Source}" +
                            $" and permissions: {string.Join(SEPARATOR, options.Permissions.ToArray())}.");
                    }
                }))
                .WithParsed((Action<UpdateOptions>)(options =>
                {
                    cliLogger.LogInformation($"{PRODUCT_NAME} {GetProductVersion()}");
                    if (!IsEntityProvided(options.Entity, cliLogger, command: "update"))
                    {
                        return;
                    }

                    bool isSuccess = ConfigGenerator.TryUpdateEntityWithOptions(options);

                    if (isSuccess)
                    {
                        cliLogger.LogInformation($"Updated the entity: {options.Entity}.");
                    }
                    else
                    {
                        cliLogger.LogError($"Could not update the entity: {options.Entity}.");
                    }
                }))
                .WithParsed((Action<StartOptions>)(options =>
                {
                    cliLogger.LogInformation($"{PRODUCT_NAME} {GetProductVersion()}");
                    bool isSuccess = ConfigGenerator.TryStartEngineWithOptions(options);

                    if (!isSuccess)
                    {
                        cliLogger.LogError("Failed to start the engine.");
                    }
                }))
                .WithParsed((Action<ExportOptions>)(options =>
                {
                    Exporter.Export(options, cliLogger);
                }))
                .WithNotParsed(err =>
                {
                    /// System.CommandLine considers --help and --version as NonParsed Errors
                    /// ref: https://github.com/commandlineparser/commandline/issues/630
                    /// This is a workaround to make sure our app exits with exit code 0,
                    /// when user does --help or --versions.
                    /// dab --help -> ErrorType.HelpVerbRequestedError
                    /// dab [command-name] --help -> ErrorType.HelpRequestedError
                    /// dab --version -> ErrorType.VersionRequestedError
                    List<Error> errors = err.ToList();
                    if (errors.Any(e => e.Tag == ErrorType.VersionRequestedError
                                        || e.Tag == ErrorType.HelpRequestedError
                                        || e.Tag == ErrorType.HelpVerbRequestedError))
                    {
                        isHelpOrVersionRequested = true;
                    }
                });

            return ((result is Parsed<object>) || (isHelpOrVersionRequested)) ? 0 : -1;
        }

        /// <summary>
        /// Check if add/update command has Entity provided. Return false otherwise.
        /// </summary>
        private static bool IsEntityProvided(string? entity, ILogger cliLogger, string command)
        {
            if (string.IsNullOrWhiteSpace(entity))
            {
                cliLogger.LogError($"Entity name is missing. " +
                            $"Usage: dab {command} [entity-name] [{command}-options]");
                return false;
            }

            return true;
        }
    }
}
