// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Core.AuthenticationHelpers;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Product;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.HealthCheck;
using Cli.Constants;
using CommandLine;
using Microsoft.AspNetCore.Authorization;
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
            ValidateOptions.AddValidFilters();
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

        /// <summary>
        /// Adds all of the class namespaces that have loggers that the user is able to change
        /// </summary>
        private static void AddValidFilters()
        {
            LoggerFilters.AddFilter(typeof(RuntimeConfigValidator).FullName);
            LoggerFilters.AddFilter(typeof(SqlQueryEngine).FullName);
            LoggerFilters.AddFilter(typeof(IQueryExecutor).FullName);
            LoggerFilters.AddFilter(typeof(ISqlMetadataProvider).FullName);
            LoggerFilters.AddFilter(typeof(BasicHealthReportResponseWriter).FullName);
            LoggerFilters.AddFilter(typeof(ComprehensiveHealthReportResponseWriter).FullName);
            LoggerFilters.AddFilter(typeof(RestController).FullName);
            LoggerFilters.AddFilter(typeof(ClientRoleHeaderAuthenticationMiddleware).FullName);
            LoggerFilters.AddFilter(typeof(ConfigurationController).FullName);
            LoggerFilters.AddFilter(typeof(IAuthorizationHandler).FullName);
            LoggerFilters.AddFilter(typeof(IAuthorizationResolver).FullName);
            LoggerFilters.AddFilter("default");
        }
    }
}
