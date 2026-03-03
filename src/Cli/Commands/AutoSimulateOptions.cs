// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Product;
using Cli.Constants;
using CommandLine;
using Microsoft.Extensions.Logging;
using static Cli.Utils;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Cli.Commands
{
    /// <summary>
    /// Command options for the autoentities-simulate verb.
    /// Simulates autoentities generation by querying the database and displaying
    /// which entities would be created for each filter definition.
    /// </summary>
    [Verb("autoentities-simulate", isDefault: false, HelpText = "Simulate autoentities generation by querying the database and displaying the results.", Hidden = false)]
    public class AutoSimulateOptions : Options
    {
        public AutoSimulateOptions(
            string? output = null,
            string? config = null)
            : base(config)
        {
            Output = output;
        }

        [Option('o', "output", Required = false, HelpText = "Path to output CSV file. If not specified, results are printed to the console.")]
        public string? Output { get; }

        public int Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            logger.LogInformation("{productName} {version}", PRODUCT_NAME, ProductInfo.GetProductVersion());
            bool isSuccess = ConfigGenerator.TrySimulateAutoentities(this, loader, fileSystem);
            if (isSuccess)
            {
                return CliReturnCode.SUCCESS;
            }
            else
            {
                logger.LogError("Failed to simulate autoentities.");
                return CliReturnCode.GENERAL_ERROR;
            }
        }
    }
}
