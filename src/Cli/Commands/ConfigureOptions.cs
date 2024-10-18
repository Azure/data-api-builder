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
    /// Configure command options
    /// This command will be used to configure non-entity config properties.
    /// </summary>
    [Verb("configure", isDefault: false, HelpText = "Configure non-entity config properties", Hidden = false)]
    public class ConfigureOptions : Options
    {
        public ConfigureOptions(
            string? dataSourceDatabaseType = null,
            string? dataSourceConnectionString = null,
            string? dataSourceOptionsDatabase = null,
            string? dataSourceOptionsContainer = null,
            string? dataSourceOptionsSchema = null,
            bool? dataSourceOptionsSetSessionContext = null,
            int? depthLimit = null,
            string? config = null)
            : base(config)
        {
            DataSourceDatabaseType = dataSourceDatabaseType;
            DataSourceConnectionString = dataSourceConnectionString;
            DataSourceOptionsDatabase = dataSourceOptionsDatabase;
            DataSourceOptionsContainer = dataSourceOptionsContainer;
            DataSourceOptionsSchema = dataSourceOptionsSchema;
            DataSourceOptionsSetSessionContext = dataSourceOptionsSetSessionContext;
            DepthLimit = depthLimit;
        }

        [Option("data-source.database-type", Required = false, HelpText = "Database type. Allowed values: MSSQL, PostgreSQL, CosmosDB_NoSQL, MySQL.")]
        public string? DataSourceDatabaseType { get; }

        [Option("data-source.connection-string", Required = false, HelpText = "Connection string for the data source.")]
        public string? DataSourceConnectionString { get; }

        [Option("data-source.options.database", Required = false, HelpText = "Database name for Cosmos DB for NoSql.")]
        public string? DataSourceOptionsDatabase { get; }

        [Option("data-source.options.container", Required = false, HelpText = "Container name for Cosmos DB for NoSql.")]
        public string? DataSourceOptionsContainer { get; }

        [Option("data-source.options.schema", Required = false, HelpText = "Schema path for Cosmos DB for NoSql.")]
        public string? DataSourceOptionsSchema { get; }

        [Option("data-source.options.set-session-context", Required = false, HelpText = "Enable session context. Allowed values: true (default), false.")]
        public bool? DataSourceOptionsSetSessionContext { get; }

        [Option("runtime.graphql.depth-limit", Required = false, HelpText = "Max allowed depth of the nested query. Allowed values: (0,2147483647] inclusive. Default is infinity. Use -1 to remove limit.")]
        public int? DepthLimit { get; }

        public int Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            logger.LogInformation("{productName} {version}", PRODUCT_NAME, ProductInfo.GetProductVersion());
            bool isSuccess = ConfigGenerator.TryConfigureSettings(this, loader, fileSystem);
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
