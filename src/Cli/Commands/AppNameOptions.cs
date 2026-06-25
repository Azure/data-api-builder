// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Configurations;
using Cli.Constants;
using CommandLine;
using Microsoft.Extensions.Logging;

namespace Cli.Commands
{
    /// <summary>
    /// Options for the <c>appname</c> command, which encodes the DAB telemetry Application Name
    /// from a config file, or decodes a telemetry Application Name into a human-readable description.
    /// </summary>
    [Verb("appname", isDefault: false, HelpText = "Show or decode the DAB telemetry 'Application Name' embedded in SQL connections.", Hidden = false)]
    public class AppNameOptions : Options
    {
        public AppNameOptions(string? decode = null, string? output = null, string? config = null)
            : base(config)
        {
            Decode = decode;
            Output = output;
        }

        /// <summary>
        /// When provided, decodes the given telemetry Application Name string into a human-readable
        /// description instead of encoding from a config file. Decoding is tolerant of truncation.
        /// </summary>
        [Option("decode", Required = false, HelpText = "Decode a telemetry Application Name string into a human-readable description.")]
        public string? Decode { get; }

        /// <summary>
        /// Optional file path to write the result to. When omitted, the result is written to stdout.
        /// </summary>
        [Option('o', "output", Required = false, HelpText = "Write the result to the specified file instead of stdout.")]
        public string? Output { get; }

        /// <summary>
        /// Handles the <c>appname</c> command.
        /// </summary>
        public int Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            // Decode mode: a pure, tolerant string decode. No config or validation is required.
            // Presence of the option (even with an empty/whitespace value) selects decode mode; the
            // decoder itself reports a friendly message for empty input.
            if (Decode is not null)
            {
                IReadOnlyList<string> decodedLines = ApplicationNameTelemetry.Decode(Decode);
                WriteResult(string.Join(Environment.NewLine, decodedLines), fileSystem, logger, trailingNewLine: true);
                return CliReturnCode.SUCCESS;
            }

            // Encode mode: parse the config and emit the telemetry Application Name.
            // We intentionally do NOT run full `validate` here — validation opens a database
            // connection, whereas encoding only needs the parsed runtime/entity settings.
            // Requiring a live database would defeat the purpose of this static inspection command.
            if (!ConfigGenerator.TryGetConfigForRuntimeEngine(Config, loader, fileSystem, out _))
            {
                logger.LogError("Could not determine the config file to use.");
                return CliReturnCode.GENERAL_ERROR;
            }

            RuntimeConfigProvider runtimeConfigProvider = new(loader);
            if (!runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig) || runtimeConfig is null)
            {
                logger.LogError("Failed to parse the config file.");
                return CliReturnCode.GENERAL_ERROR;
            }

            // There is no live connection context at design time, so the context fields
            // (Protocol/Object/Source/Role) are emitted as placeholders.
            string telemetryAppName = ApplicationNameTelemetry.EncodeTelemetryString(runtimeConfig, liveDataSource: null);
            WriteResult(telemetryAppName, fileSystem, logger, trailingNewLine: false);
            return CliReturnCode.SUCCESS;
        }

        /// <summary>
        /// Writes the result to the output file when <c>--output</c> is provided, otherwise to stdout.
        /// </summary>
        private void WriteResult(string content, IFileSystem fileSystem, ILogger logger, bool trailingNewLine)
        {
            if (!string.IsNullOrWhiteSpace(Output))
            {
                // Mirror stdout behavior: append a trailing newline for human-readable (decode) output,
                // but keep encode output exact (no trailing newline) so it can be copied/piped verbatim.
                string fileContent = trailingNewLine ? content + Environment.NewLine : content;
                fileSystem.File.WriteAllText(Output, fileContent);
                logger.LogInformation("Wrote output to '{outputFile}'.", Output);
            }
            else if (trailingNewLine)
            {
                Console.WriteLine(content);
            }
            else
            {
                Console.Write(content);
            }
        }
    }
}
