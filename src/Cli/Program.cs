// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Cli.Commands;
using Cli.Constants;
using Cli.Telemetry;
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

            // Parse MCP and LogLevel flags in a single pass for efficiency.
            // These flags need to be known before logger creation.
            ParseEarlyFlags(args);

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

            using CliTelemetrySession telemetry = CliTelemetryHosting.CreateStandalone();
            try
            {
                return Execute(args, cliLogger, fileSystem, loader, telemetry);
            }
            finally
            {
                // Completion belongs to Execute; Main owns the bounded drain and disposal.
                telemetry.StopAsync().GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Parses flags that need to be known before logger creation.
        /// Scans args in a single pass for efficiency.
        /// </summary>
        /// <param name="args">Command line arguments</param>
        private static void ParseEarlyFlags(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (string.Equals(arg, "--mcp-stdio", StringComparison.OrdinalIgnoreCase))
                {
                    Utils.IsMcpStdioMode = true;
                }
                else if (string.Equals(arg, "--log-level", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    Utils.IsCliOverriding = true;
                    if (Enum.TryParse<LogLevel>(args[i + 1], ignoreCase: true, out LogLevel cliLogLevel))
                    {
                        Utils.CliLogLevel = cliLogLevel;
                    }
                }
            }
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
            => Execute(args, cliLogger, fileSystem, loader, telemetry: null);

        /// <summary>
        /// Executes one invocation with an explicitly supplied session. Never creates a session,
        /// reads telemetry environment settings, or stops a caller-owned sender.
        /// Optional launch/export dependencies are invocation-local; production callers omit them.
        /// </summary>
        internal static int Execute(string[] args, ILogger cliLogger, IFileSystem fileSystem,
            FileSystemRuntimeConfigLoader loader, CliTelemetrySession? telemetry,
            Func<string[], ProductTelemetryLaunchContext?, Action?, bool>? engineLauncher = null,
            Func<Exporter>? exporterFactory = null, CancellationTokenSource? exportCancellationTokenSource = null)
        {
            CliTelemetryCommand? command = null;
            if (telemetry is { IsEnabled: true })
            {
                try
                {
                    command = CliTelemetryCommand.Inspect(args) with { Control = "none" };
                }
                catch (Exception)
                {
                    // Grammar inspection is optional and must not change the real parser's behavior.
                    telemetry.Disable();
                }
            }

            CliTelemetryOutcome outcome = CliTelemetryOutcome.Unknown;
            CliTelemetryFailureCategory failureCategory = CliTelemetryFailureCategory.Unknown;
            CliTelemetryCommandResult? terminalResult = null;
            try
            {
                Parser parser = new(settings =>
                {
                    settings.CaseInsensitiveEnumValues = true;
                    settings.HelpWriter = Console.Out;
                });

                // The one real parse remains authoritative; inspection never constructs options.
                int result = parser.ParseArguments<InitOptions, AddOptions, UpdateOptions, StartOptions, ValidateOptions, ExportOptions, AddTelemetryOptions, ConfigureOptions, AutoConfigOptions, AutoConfigSimulateOptions, AppNameOptions>(args)
                    .MapResult(
                        (InitOptions options) => Invoke(options, () => options.Handler(cliLogger, loader, fileSystem)),
                        (AddOptions options) => Invoke(options, () => options.Handler(cliLogger, loader, fileSystem)),
                        (UpdateOptions options) => Invoke(options, () => options.Handler(cliLogger, loader, fileSystem)),
                        (StartOptions options) => Invoke(options, () => options.Handler(cliLogger, loader, fileSystem)),
                        (ValidateOptions options) => Invoke(options, () => options.Handler(cliLogger, loader, fileSystem)),
                        (AddTelemetryOptions options) => Invoke(options, () => options.Handler(cliLogger, loader, fileSystem)),
                        (ConfigureOptions options) => Invoke(options, () => options.Handler(cliLogger, loader, fileSystem)),
                        (AutoConfigOptions options) => Invoke(options, () => options.Handler(cliLogger, loader, fileSystem)),
                        (AutoConfigSimulateOptions options) => Invoke(options, () => options.Handler(cliLogger, loader, fileSystem)),
                        (ExportOptions options) => Invoke(options, () => options.Handler(cliLogger, loader, fileSystem)),
                        (AppNameOptions options) => Invoke(options, () => options.Handler(cliLogger, loader, fileSystem)),
                        errors =>
                        {
                            List<Error> parseErrors = errors.ToList();
                            string control = parseErrors.Any(error => error.Tag is ErrorType.HelpRequestedError or ErrorType.HelpVerbRequestedError)
                                ? "help"
                                : parseErrors.Any(error => error.Tag == ErrorType.VersionRequestedError) ? "version" : "none";
                            if (command is not null)
                            {
                                command = command with { Control = control };
                            }

                            if (control == "none")
                            {
                                telemetry?.MarkFailure(CliTelemetryOutcome.ParseFailure, CliTelemetryFailureCategory.Arguments);
                            }

                            return DabCliParserErrorHandler.ProcessErrorsAndReturnExitCode(parseErrors);
                        });

                if (terminalResult is CliTelemetryCommandResult terminal)
                {
                    // A final export observation distinguishes no schema from a successful
                    // retry without changing the legacy exit code or consulting earlier errors.
                    outcome = terminal.Outcome;
                    failureCategory = terminal.FailureCategory;
                }
                else if (command?.Name == "start" && telemetry?.HasEngineStartupFailed == true)
                {
                    // The web host can return normally after StopApplication during failed
                    // initialization. Keep that legacy exit code but report the observed failure.
                    outcome = CliTelemetryOutcome.ExecutionFailure;
                    failureCategory = CliTelemetryFailureCategory.Initialization;
                }
                else if (result == CliReturnCode.SUCCESS)
                {
                    // Recovered attempts are not failed commands. For start, this runs only
                    // after the actual engine lifetime has returned to its handler.
                    outcome = CliTelemetryOutcome.Success;
                    failureCategory = CliTelemetryFailureCategory.None;
                }
                else
                {
                    UseFirstFailure();
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                outcome = CliTelemetryOutcome.Canceled;
                failureCategory = CliTelemetryFailureCategory.Canceled;
                telemetry?.MarkFailure(outcome, failureCategory);
                throw;
            }
            catch (Exception exception)
            {
                CliTelemetryHosting.MarkException(telemetry, exception);
                UseFirstFailure();
                throw;
            }
            finally
            {
                if (command is not null)
                {
                    telemetry?.Complete(command.Name, command.Control, command.Options, outcome, failureCategory);
                }
            }

            int Invoke(Options options, Func<int> handler)
            {
                options.ProductTelemetry = telemetry;
                options.EngineLauncher = engineLauncher;
                options.ExporterFactory = exporterFactory;
                options.ExportCancellationTokenSource = exportCancellationTokenSource;
                int result = handler();
                if (options is ExportOptions exportOptions)
                {
                    terminalResult = exportOptions.TerminalTelemetryResult;
                }

                // These handlers reject the missing positional entity before ConfigGenerator.
                if (result != CliReturnCode.SUCCESS && options is EntityOptions entityOptions
                    && string.IsNullOrWhiteSpace(entityOptions.Entity))
                {
                    telemetry?.MarkFailure(CliTelemetryOutcome.ValidationFailure, CliTelemetryFailureCategory.Arguments);
                }

                return result;
            }

            void UseFirstFailure()
            {
                if (telemetry is { HasFailure: true })
                {
                    outcome = telemetry.FailureOutcome;
                    failureCategory = telemetry.FailureCategory;
                }
            }
        }
    }
}
