// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Product;
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

        public void Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
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
        }
    }
}