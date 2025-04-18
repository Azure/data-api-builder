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
    /// Update command options
    /// </summary>
    [Verb("update", isDefault: false, HelpText = "Update an existing entity in the configuration file.", Hidden = false)]
    public class UpdateOptions : EntityOptions
    {
        public UpdateOptions(
            string? source,
            IEnumerable<string>? permissions,
            string? relationship,
            string? cardinality,
            string? targetEntity,
            string? linkingObject,
            IEnumerable<string>? linkingSourceFields,
            IEnumerable<string>? linkingTargetFields,
            IEnumerable<string>? relationshipFields,
            IEnumerable<string>? map,
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
            string config)
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
                  config)
        {
            Source = source;
            Permissions = permissions;
            Relationship = relationship;
            Cardinality = cardinality;
            TargetEntity = targetEntity;
            LinkingObject = linkingObject;
            LinkingSourceFields = linkingSourceFields;
            LinkingTargetFields = linkingTargetFields;
            RelationshipFields = relationshipFields;
            Map = map;
        }

        [Option('s', "source", Required = false, HelpText = "Name of the source table or container.")]
        public string? Source { get; }

        [Option("permissions", Required = false, Separator = ':', HelpText = "Permissions required to access the source table or container.")]
        public IEnumerable<string>? Permissions { get; }

        [Option("relationship", Required = false, HelpText = "Specify relationship between two entities.")]
        public string? Relationship { get; }

        [Option("cardinality", Required = false, HelpText = "Specify cardinality between two entities.")]
        public string? Cardinality { get; }

        [Option("target.entity", Required = false, HelpText = "Another exposed entity to which the source entity relates to.")]
        public string? TargetEntity { get; }

        [Option("linking.object", Required = false, HelpText = "Database object that is used to support an M:N relationship.")]
        public string? LinkingObject { get; }

        [Option("linking.source.fields", Required = false, Separator = ',', HelpText = "Database fields in the linking object to connect to the related item in the source entity.")]
        public IEnumerable<string>? LinkingSourceFields { get; }

        [Option("linking.target.fields", Required = false, Separator = ',', HelpText = "Database fields in the linking object to connect to the related item in the target entity.")]
        public IEnumerable<string>? LinkingTargetFields { get; }

        [Option("relationship.fields", Required = false, Separator = ':', HelpText = "Specify fields to be used for mapping the entities.")]
        public IEnumerable<string>? RelationshipFields { get; }

        [Option('m', "map", Separator = ',', Required = false, HelpText = "Specify mappings between database fields and GraphQL and REST fields. format: --map \"backendName1:exposedName1,backendName2:exposedName2,...\".")]
        public IEnumerable<string>? Map { get; }

        public int Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            logger.LogInformation("{productName} {version}", PRODUCT_NAME, ProductInfo.GetProductVersion());
            if (!IsEntityProvided(Entity, logger, command: "update"))
            {
                return CliReturnCode.GENERAL_ERROR;
            }

            bool isSuccess = ConfigGenerator.TryUpdateEntityWithOptions(this, loader, fileSystem);

            if (isSuccess)
            {
                logger.LogInformation("Updated the entity: {Entity}.", Entity);
            }
            else
            {
                logger.LogError("Could not update the entity: {Entity}.", Entity);
            }

            return isSuccess ? CliReturnCode.SUCCESS : CliReturnCode.GENERAL_ERROR;
        }
    }
}
