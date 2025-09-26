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
    /// Add command options
    /// </summary>
    [Verb("add", isDefault: false, HelpText = "Add a new entity to the configuration file.", Hidden = false)]
    public class AddOptions : EntityOptions
    {
        public AddOptions(
            string source,
            IEnumerable<string> permissions,
            string entity,
            string? sourceType,
            IEnumerable<string>? sourceParameters,
            IEnumerable<string>? sourceKeyFields,
            string? restRoute,
            IEnumerable<string>? restMethodsForStoredProcedure,
            string? graphQLType,
            string? graphQLOperationForStoredProcedure,
            IEnumerable<string>? fieldsToInclude,
            IEnumerable<string>? fieldsToExclude,
            string? policyRequest,
            string? policyDatabase,
            string? cacheEnabled,
            string? cacheTtl,
            string? config,
            string? description)
            : base(entity,
                  sourceType,
                  sourceParameters,
                  sourceKeyFields,
                  restRoute,
                  restMethodsForStoredProcedure,
                  graphQLType,
                  graphQLOperationForStoredProcedure,
                  fieldsToInclude,
                  fieldsToExclude,
                  policyRequest,
                  policyDatabase,
                  cacheEnabled,
                  cacheTtl,
                  config,
                  description)
        {
            Source = source;
            Permissions = permissions;
        }

        [Option('s', "source", Required = true, HelpText = "Name of the source database object.")]
        public string Source { get; }

        [Option("permissions", Required = true, Separator = ':', HelpText = "Permissions required to access the source table or container.")]
        public IEnumerable<string> Permissions { get; }

        public int Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            logger.LogInformation("{productName} {version}", PRODUCT_NAME, ProductInfo.GetProductVersion());
            if (!IsEntityProvided(Entity, logger, command: "add"))
            {
                return -1;
            }

            bool isSuccess = ConfigGenerator.TryAddEntityToConfigWithOptions(this, loader, fileSystem);
            if (isSuccess)
            {
                logger.LogInformation("Added new entity: {Entity} with source: {Source} and permissions: {permissions}.", Entity, Source, string.Join(SEPARATOR, Permissions));
                logger.LogInformation("SUGGESTION: Use 'dab update [entity-name] [options]' to update any entities in your config.");
            }
            else
            {
                logger.LogError("Could not add entity: {Entity} with source: {Source} and permissions: {permissions}.", Entity, Source, string.Join(SEPARATOR, Permissions));
            }

            return isSuccess ? CliReturnCode.SUCCESS : CliReturnCode.GENERAL_ERROR;
        }
    }
}
