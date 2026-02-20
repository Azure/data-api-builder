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
    /// AutoConfigOptions command options
    /// This command will be used to configure autoentities definitions in the config file.
    /// </summary>
    [Verb("auto-config", isDefault: false, HelpText = "Configure autoentities definitions", Hidden = false)]
    public class AutoConfigOptions : Options
    {
        public AutoConfigOptions(
            string definitionName,
            IEnumerable<string>? patternsInclude = null,
            IEnumerable<string>? patternsExclude = null,
            string? patternsName = null,
            string? templateMcpDmlTool = null,
            bool? templateRestEnabled = null,
            bool? templateGraphqlEnabled = null,
            bool? templateCacheEnabled = null,
            int? templateCacheTtlSeconds = null,
            string? templateCacheLevel = null,
            bool? templateHealthEnabled = null,
            IEnumerable<string>? permissions = null,
            string? config = null)
            : base(config)
        {
            DefinitionName = definitionName;
            PatternsInclude = patternsInclude;
            PatternsExclude = patternsExclude;
            PatternsName = patternsName;
            TemplateMcpDmlTool = templateMcpDmlTool;
            TemplateRestEnabled = templateRestEnabled;
            TemplateGraphqlEnabled = templateGraphqlEnabled;
            TemplateCacheEnabled = templateCacheEnabled;
            TemplateCacheTtlSeconds = templateCacheTtlSeconds;
            TemplateCacheLevel = templateCacheLevel;
            TemplateHealthEnabled = templateHealthEnabled;
            Permissions = permissions;
        }

        [Value(0, Required = true, HelpText = "Name of the autoentities definition to configure.")]
        public string DefinitionName { get; }

        [Option("patterns.include", Required = false, HelpText = "T-SQL LIKE pattern(s) to include database objects. Space-separated array of patterns.")]
        public IEnumerable<string>? PatternsInclude { get; }

        [Option("patterns.exclude", Required = false, HelpText = "T-SQL LIKE pattern(s) to exclude database objects. Space-separated array of patterns.")]
        public IEnumerable<string>? PatternsExclude { get; }

        [Option("patterns.name", Required = false, HelpText = "Interpolation syntax for entity naming (must be unique for each generated entity).")]
        public string? PatternsName { get; }

        [Option("template.mcp.dml-tool", Required = false, HelpText = "Enable/disable DML tools for generated entities. Allowed values: true, false.")]
        public string? TemplateMcpDmlTool { get; }

        [Option("template.rest.enabled", Required = false, HelpText = "Enable/disable REST endpoint for generated entities. Allowed values: true, false.")]
        public bool? TemplateRestEnabled { get; }

        [Option("template.graphql.enabled", Required = false, HelpText = "Enable/disable GraphQL endpoint for generated entities. Allowed values: true, false.")]
        public bool? TemplateGraphqlEnabled { get; }

        [Option("template.cache.enabled", Required = false, HelpText = "Enable/disable cache for generated entities. Allowed values: true, false.")]
        public bool? TemplateCacheEnabled { get; }

        [Option("template.cache.ttl-seconds", Required = false, HelpText = "Cache time-to-live in seconds for generated entities.")]
        public int? TemplateCacheTtlSeconds { get; }

        [Option("template.cache.level", Required = false, HelpText = "Cache level for generated entities. Allowed values: L1, L1L2.")]
        public string? TemplateCacheLevel { get; }

        [Option("template.health.enabled", Required = false, HelpText = "Enable/disable health check for generated entities. Allowed values: true, false.")]
        public bool? TemplateHealthEnabled { get; }

        [Option("permissions", Required = false, Separator = ':', HelpText = "Permissions for generated entities in the format role:actions (e.g., anonymous:read).")]
        public IEnumerable<string>? Permissions { get; }

        public int Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            logger.LogInformation("{productName} {version}", PRODUCT_NAME, ProductInfo.GetProductVersion());
            bool isSuccess = ConfigGenerator.TryConfigureAutoentities(this, loader, fileSystem);
            if (isSuccess)
            {
                logger.LogInformation("Successfully configured autoentities definition: {DefinitionName}.", DefinitionName);
                return CliReturnCode.SUCCESS;
            }
            else
            {
                logger.LogError("Failed to configure autoentities definition: {DefinitionName}.", DefinitionName);
                return CliReturnCode.GENERAL_ERROR;
            }
        }
    }
}
