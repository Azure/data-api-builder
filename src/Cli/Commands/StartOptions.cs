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
    /// Start command options
    /// </summary>
    [Verb("start", isDefault: false, HelpText = "Start Data Api Builder Engine", Hidden = false)]
    public class StartOptions : Options
    {
        private const string LOGLEVEL_HELPTEXT = "Specifies logging level as provided value. For possible values, see: https://go.microsoft.com/fwlink/?linkid=2263106";

        public StartOptions(bool verbose, LogLevel? logLevel, bool isHttpsRedirectionDisabled, string config)
            : base(config)
        {
            // When verbose is true we set LogLevel to information.
            LogLevel = verbose is true ? Microsoft.Extensions.Logging.LogLevel.Information : logLevel;
            IsHttpsRedirectionDisabled = isHttpsRedirectionDisabled;
        }

        // SetName defines mutually exclusive sets, ie: can not have
        // both verbose and LogLevel.
        [Option("verbose", SetName = "verbose", Required = false, HelpText = "Specifies logging level as informational.")]
        public bool Verbose { get; }

        [Option("LogLevel", SetName = "LogLevel", Required = false, HelpText = LOGLEVEL_HELPTEXT)]
        public LogLevel? LogLevel { get; }

        [Option("no-https-redirect", Required = false, HelpText = "Disables automatic https redirects.")]
        public bool IsHttpsRedirectionDisabled { get; }

        public void Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            logger.LogInformation("{productName} {version}", PRODUCT_NAME, ProductInfo.GetProductVersion());
            bool isSuccess = ConfigGenerator.TryStartEngineWithOptions(this, loader, fileSystem);

            if (!isSuccess)
            {
                logger.LogError("Failed to start the engine.");
            }
        }
    }
}
