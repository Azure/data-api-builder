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
    /// Validate command options
    /// </summary>
    [Verb("validate", isDefault: false, HelpText = "Validate config for Data Api Builder Engine", Hidden = false)]
    public class ValidateOptions : Options
    {
        public ValidateOptions(string config)
            : base(config)
        { }

        /// <summary>
        /// This Handler method is responsible for validating the config file and is called when `validate`
        /// command is invoked.
        /// </summary>
        public int Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            logger.LogInformation("{productName} {version}", PRODUCT_NAME, ProductInfo.GetProductVersion());
            bool isValidConfig = ConfigGenerator.IsConfigValid(this, loader, fileSystem);

            if (isValidConfig)
            {
                logger.LogInformation("Config is valid.");
            }
            else
            {
                logger.LogError("Config is invalid. Check above logs for details.");
            }

            return isValidConfig ? CliReturnCode.SUCCESS : CliReturnCode.GENERAL_ERROR;
        }
    }
}
