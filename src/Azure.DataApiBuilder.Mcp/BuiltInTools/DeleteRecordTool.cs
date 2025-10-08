// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.BuiltInTools
{
    /// <summary>
    /// Tool to delete records from a table/view entity configured in DAB.
    /// Supports both simple and composite primary keys.
    /// </summary>
    public class DeleteRecordTool : IMcpTool
    {
        /// <summary>
        /// Gets the type of the tool, which is BuiltIn for this implementation.
        /// </summary>
        public ToolType ToolType { get; } = ToolType.BuiltIn;

        /// <summary>
        /// Gets the metadata for the delete-record tool, including its name, description, and input schema.
        /// </summary>
        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "delete-record",
                Description = "Deletes a record from a table based on primary key or composite key",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""The name of the entity (table) as configured in dab-config""
                            },
                            ""keys"": {
                                ""type"": ""object"",
                                ""description"": ""Primary key values to identify the record to delete. For composite keys, provide all key columns as properties""
                            }
                        },
                        ""required"": [""entity"", ""keys""]
                    }"
                )
            };
        }

        /// <summary>
        /// Executes the delete-record tool, deleting an existing record in the specified entity using provided keys.
        /// </summary>
        public async Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            ILogger<DeleteRecordTool>? logger = serviceProvider.GetService<ILogger<DeleteRecordTool>>();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1) Parsing & basic argument validation
                if (arguments is null)
                {
                    return McpResponseBuilder.BuildErrorResult("InvalidArguments", "No arguments provided.", logger);
                }

                if (!McpArgumentParser.TryParseEntityAndKeys(arguments.RootElement, out string entityName, out Dictionary<string, object?> keys, out string parseError))
                {
                    return McpResponseBuilder.BuildErrorResult("InvalidArguments", parseError, logger);
                }

                // 2) Resolve required services & configuration
                RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig config = runtimeConfigProvider.GetConfig();

                IMetadataProviderFactory metadataProviderFactory = serviceProvider.GetRequiredService<IMetadataProviderFactory>();
                IMutationEngineFactory mutationEngineFactory = serviceProvider.GetRequiredService<IMutationEngineFactory>();

                // 3) Resolve metadata for entity existence check
                string dataSourceName;
                ISqlMetadataProvider sqlMetadataProvider;

                try
                {
                    dataSourceName = config.GetDataSourceNameFromEntityName(entityName);
                    sqlMetadataProvider = metadataProviderFactory.GetMetadataProvider(dataSourceName);
                }
                catch (Exception)
                {
                    return McpResponseBuilder.BuildErrorResult("EntityNotFound", $"Entity '{entityName}' is not defined in the configuration.", logger);
                }

                if (!sqlMetadataProvider.EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? dbObject) || dbObject is null)
                {
                    return McpResponseBuilder.BuildErrorResult("EntityNotFound", $"Entity '{entityName}' is not defined in the configuration.", logger);
                }

                // Validate it's a table or view
                if (dbObject.SourceType != EntitySourceType.Table && dbObject.SourceType != EntitySourceType.View)
                {
                    return McpResponseBuilder.BuildErrorResult("InvalidEntity", $"Entity '{entityName}' is not a table or view. Use 'execute-entity' for stored procedures.", logger);
                }

                // 4) Authorization
                IAuthorizationResolver authResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext? httpContext = httpContextAccessor.HttpContext;

                if (!McpAuthorizationHelper.ValidateRoleContext(httpContext, authResolver, out string roleError))
                {
                    return McpResponseBuilder.BuildErrorResult("PermissionDenied", $"Permission denied: {roleError}", logger);
                }

                if (!McpAuthorizationHelper.TryResolveAuthorizedRole(
                    httpContext!,
                    authResolver,
                    entityName,
                    EntityActionOperation.Delete,
                    out string? effectiveRole,
                    out string authError))
                {
                    return McpResponseBuilder.BuildErrorResult("PermissionDenied", $"Permission denied: {authError}", logger);
                }

                // 5) Build and validate Delete context
                RequestValidator requestValidator = new(metadataProviderFactory, runtimeConfigProvider);

                DeleteRequestContext context = new(
                    entityName: entityName,
                    dbo: dbObject,
                    isList: false);

                foreach (KeyValuePair<string, object?> kvp in keys)
                {
                    if (kvp.Value is null)
                    {
                        return McpResponseBuilder.BuildErrorResult("InvalidArguments", $"Primary key value for '{kvp.Key}' cannot be null.", logger);
                    }

                    context.PrimaryKeyValuePairs[kvp.Key] = kvp.Value;
                }

                requestValidator.ValidatePrimaryKey(context);

                // 6) Execute
                DatabaseType dbType = config.GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
                IMutationEngine mutationEngine = mutationEngineFactory.GetMutationEngine(dbType);

                IActionResult? mutationResult = null;
                try
                {
                    mutationResult = await mutationEngine.ExecuteAsync(context).ConfigureAwait(false);
                }
                catch (DataApiBuilderException dabEx) when (dabEx.Message.Contains("Could not find item with", StringComparison.OrdinalIgnoreCase))
                {
                    string keyDetails = McpJsonHelper.FormatKeyDetails(keys);
                    return McpResponseBuilder.BuildErrorResult(
                        "RecordNotFound",
                        $"No record found with the specified primary key: {keyDetails}",
                        logger);
                }
                catch (Exception ex)
                {
                    string errorMsg = ex.Message ?? string.Empty;

                    if (errorMsg.Contains("Could not find", StringComparison.OrdinalIgnoreCase) ||
                        errorMsg.Contains("record not found", StringComparison.OrdinalIgnoreCase))
                    {
                        return McpResponseBuilder.BuildErrorResult(
                            "RecordNotFound",
                            "No entity found with the given key.",
                            logger);
                    }
                    else
                    {
                        throw;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 7) Build response
                return BuildDeleteSuccessResponse(entityName, keys, mutationResult, logger);
            }
            catch (OperationCanceledException)
            {
                return McpResponseBuilder.BuildErrorResult("OperationCanceled", "The delete operation was canceled.", logger: null);
            }
            catch (ArgumentException argEx)
            {
                return McpResponseBuilder.BuildErrorResult("InvalidArguments", argEx.Message, logger);
            }
            catch (Exception ex)
            {
                ILogger<DeleteRecordTool>? innerLogger = serviceProvider.GetService<ILogger<DeleteRecordTool>>();
                innerLogger?.LogError(ex, "Unexpected error in DeleteRecordTool.");

                return McpResponseBuilder.BuildErrorResult(
                    "UnexpectedError",
                    ex.Message ?? "An unexpected error occurred during the delete operation.",
                    logger: null);
            }
        }

        private static CallToolResult BuildDeleteSuccessResponse(
            string entityName,
            Dictionary<string, object?> keys,
            IActionResult? mutationResult,
            ILogger? logger)
        {
            Dictionary<string, object?> responseData = new()
            {
                ["entity"] = entityName,
                ["keyDetails"] = McpJsonHelper.FormatKeyDetails(keys),
                ["message"] = "Record deleted successfully"
            };

            // Handle different result types
            if (mutationResult is OkObjectResult okObjectResult)
            {
                string rawPayloadJson = McpResponseBuilder.ExtractResultJson(okObjectResult);
                using JsonDocument resultDoc = JsonDocument.Parse(rawPayloadJson);
                JsonElement root = resultDoc.RootElement;

                Dictionary<string, object?> extractedData = McpJsonHelper.ExtractValuesFromEngineResult(root);
                if (extractedData.Count > 0)
                {
                    responseData["result"] = extractedData;
                }
            }

            return McpResponseBuilder.BuildSuccessResult(
                responseData,
                logger,
                $"DeleteRecordTool success for entity {entityName}."
            );
        }
    }
}
