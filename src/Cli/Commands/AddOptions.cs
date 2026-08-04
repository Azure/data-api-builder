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
            string? cacheTtlSeconds,
            string? cacheLevel,
            string? healthEnabled,
            string? semanticSearchEnabled = null,
            string? semanticSearchRedisIndexName = null,
            string? semanticSearchRedisIndexType = null,
            string? semanticSearchRedisIndexMultiplier = null,
            string? semanticSearchSimilarityThreshold = null,
            string? semanticSearchInputDescription = null,
            string? semanticSearchOutputDescription = null,
            string? description = null,
            IEnumerable<string>? parametersNameCollection = null,
            IEnumerable<string>? parametersDescriptionCollection = null,
            IEnumerable<string>? parametersRequiredCollection = null,
            IEnumerable<string>? parametersDefaultCollection = null,
            IEnumerable<string>? fieldsNameCollection = null,
            IEnumerable<string>? fieldsAliasCollection = null,
            IEnumerable<string>? fieldsDescriptionCollection = null,
            IEnumerable<bool>? fieldsPrimaryKeyCollection = null,
            string? mcpDmlTools = null,
            string? mcpCustomTool = null,
            string? config = null
        )
        : base(
            entity,
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
            cacheTtlSeconds,
            cacheLevel,
            healthEnabled,
            semanticSearchEnabled,
            semanticSearchRedisIndexName,
            semanticSearchRedisIndexType,
            semanticSearchRedisIndexMultiplier,
            semanticSearchSimilarityThreshold,
            semanticSearchInputDescription,
            semanticSearchOutputDescription,
            description,
            parametersNameCollection,
            parametersDescriptionCollection,
            parametersRequiredCollection,
            parametersDefaultCollection,
            fieldsNameCollection,
            fieldsAliasCollection,
            fieldsDescriptionCollection,
            fieldsPrimaryKeyCollection,
            mcpDmlTools,
            mcpCustomTool,
            config
        )
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
                logger.LogInformation("Added new entity: {Entity} with source: {Source} and permissions: {permissions}.",
                    Entity, Source, string.Join(SEPARATOR, Permissions));
                logger.LogInformation("SUGGESTION: Use 'dab update [entity-name] [options]' to update any entities in your config.");
            }
            else
            {
                logger.LogError("Could not add entity: {Entity} with source: {Source} and permissions: {permissions}.",
                    Entity, Source, string.Join(SEPARATOR, Permissions));
            }

            return isSuccess ? CliReturnCode.SUCCESS : CliReturnCode.GENERAL_ERROR;
        }
    }
}
