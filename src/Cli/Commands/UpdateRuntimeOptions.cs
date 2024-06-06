// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Product;
using Cli.Constants;
using CommandLine;
using Microsoft.Extensions.Logging;
using static Cli.Utils;

namespace Cli.Commands
{
    /// <summary>
    /// Init command options
    /// </summary>
    [Verb("runtime", isDefault: false, HelpText = "Update runtime settings for the config file.", Hidden = false)]
    public class UpdateRuntimeOptions : Options
    {
        public UpdateRuntimeOptions(
            int? depthLimit = null,
            string? config = null)
            : base(config)
        {
            DepthLimit = depthLimit;
        }

        [Option("depth-limit", Required = false, HelpText = "Max allowed depth of the nested query. Allowed values: [0,2147483647] inclusive. Default is infinity. Use 0 to remove limit.")]
        public int? DepthLimit { get; }

        public int Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            logger.LogInformation("{productName} {version}", PRODUCT_NAME, ProductInfo.GetProductVersion());
            bool isSuccess = ConfigGenerator.TryUpdateRuntimeSettings(this, loader, fileSystem);
            if (isSuccess)
            {
                logger.LogInformation("Successfully updated runtime settings in the config file.");
                return CliReturnCode.SUCCESS;
            }
            else
            {
                logger.LogError("Failed to update runtime settings in the config file.");
                return CliReturnCode.GENERAL_ERROR;
            }
        }
    }
}
