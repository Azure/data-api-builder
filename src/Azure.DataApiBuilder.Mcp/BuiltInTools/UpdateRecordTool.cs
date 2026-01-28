// Copyright (c) Microsoft.
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.BuiltInTools
{
    /// <summary>
    /// Updates an existing record in the specified entity using provided keys (PKs) and fields (new values).
    /// Input schema:
    /// {
    ///   "entity": "EntityName",
    ///   "keys":   { "Id": 42, "TenantId": "ABC" },
    ///   "fields": { "Status": "Closed", "Comment": "Done" }
    /// }
    /// </summary>
    public class UpdateRecordTool : IMcpTool
    {
        /// <summary>
        /// Gets the type of the tool, which is BuiltIn for this implementation.
        /// </summary>
        public ToolType ToolType { get; } = ToolType.BuiltIn;

        /// <summary>
        /// Gets the metadata for the update_record tool, including its name, description, and input schema.
        /// </summary>
        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "update_record",
                Description = "STEP 1: describe_entities -> find entities with UPDATE permission and their key fields. STEP 2: call this tool with keys and new field values.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""Entity name with UPDATE permission.""
                            },
                            ""keys"": {
                                ""type"": ""object"",
                                ""description"": ""Primary or composite keys identifying the record.""
                            },
                            ""fields"": {
                                ""type"": ""object"",
                                ""description"": ""Fields and their new values.""
                            }
                        },
                        ""required"": [""entity"", ""keys"", ""fields""]
                    }"
                )
            };
        }

        /// <summary>
        /// Executes the update_record tool, updating an existing record in the specified entity using provided keys and fields.
        /// </summary>
        /// <param name="arguments">The JSON arguments containing entity, keys, and fields.</param>
        /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="CallToolResult"/> representing the outcome of the update operation.</returns>
        public async Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            ILogger<UpdateRecordTool>? logger = serviceProvider.GetService<ILogger<UpdateRecordTool>>();
            string toolName = GetToolMetadata().Name;

            RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
            RuntimeConfig config = runtimeConfigProvider.GetConfig();

            // 2)Check if the tool is enabled in configuration before proceeding.
            if (config.McpDmlTools?.UpdateRecord != true)
            {
                return McpErrorHelpers.ToolDisabled(GetToolMetadata().Name, logger);
            }

            try
            {

                cancellationToken.ThrowIfCancellationRequested();

                // 3) Parsing & basic argument validation (entity, keys, fields)
                if (arguments is null)
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "No arguments provided.", logger);
                }

                if (!McpArgumentParser.TryParseEntityKeysAndFields(
                        arguments.RootElement,
                        out string entityName,
                        out Dictionary<string, object?> keys,
                        out Dictionary<string, object?> fields,
                        out string parseError))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", parseError, logger);
                }

                // Check entity-level DML tool configuration
                if (config.Entities?.TryGetValue(entityName, out Entity? entity) == true)
                {
                    if (entity.Mcp?.DmlToolEnabled == false)
                    {
                        return McpErrorHelpers.ToolDisabled(toolName, logger, $"DML tools are disabled for entity '{entityName}'.");
                    }
                }

                IMetadataProviderFactory metadataProviderFactory = serviceProvider.GetRequiredService<IMetadataProviderFactory>();
                IMutationEngineFactory mutationEngineFactory = serviceProvider.GetRequiredService<IMutationEngineFactory>();

                if (!McpMetadataHelper.TryResolveMetadata(
                        entityName,
                        config,
                        serviceProvider,
                        out ISqlMetadataProvider sqlMetadataProvider,
                        out DatabaseObject dbObject,
                        out string dataSourceName,
                        out string metadataError))
                {
                    return McpResponseBuilder.BuildErrorResult(toolName, "EntityNotFound", metadataError, logger);
                }

                // 5) Authorization after we have a known entity
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext? httpContext = httpContextAccessor.HttpContext;
                IAuthorizationResolver authResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();

                if (httpContext is null || !authResolver.IsValidRoleContext(httpContext))
                {
                    return McpErrorHelpers.PermissionDenied(toolName, entityName, "update", "unable to resolve a valid role context for update operation.", logger);
                }

                if (!McpAuthorizationHelper.TryResolveAuthorizedRole(
                    httpContext!,
                    authResolver,
                    entityName,
                    EntityActionOperation.Update,
                    out string? effectiveRole,
                    out string authError))
                {
                    return McpErrorHelpers.PermissionDenied(toolName, entityName, "update", authError, logger);
                }

                // 6) Build and validate Upsert (UpdateIncremental) context
                JsonElement upsertPayloadRoot = RequestValidator.ValidateAndParseRequestBody(JsonSerializer.Serialize(fields));
                RequestValidator requestValidator = new(metadataProviderFactory, runtimeConfigProvider);

                UpsertRequestContext context = new(
                    entityName: entityName,
                    dbo: dbObject,
                    insertPayloadRoot: upsertPayloadRoot,
                    operationType: EntityActionOperation.UpdateIncremental);

                foreach (KeyValuePair<string, object?> kvp in keys)
                {
                    if (kvp.Value is null)
                    {
                        return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", $"Primary key value for '{kvp.Key}' cannot be null.", logger);
                    }

                    context.PrimaryKeyValuePairs[kvp.Key] = kvp.Value;
                }

                if (context.DatabaseObject.SourceType is EntitySourceType.Table)
                {
                    requestValidator.ValidateUpsertRequestContext(context);
                }

                requestValidator.ValidatePrimaryKey(context);

                // 7) Execute
                DatabaseType dbType = config.GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
                IMutationEngine mutationEngine = mutationEngineFactory.GetMutationEngine(dbType);

                IActionResult? mutationResult = null;
                try
                {
                    mutationResult = await mutationEngine.ExecuteAsync(context).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    string errorMsg = ex.Message ?? string.Empty;

                    if (errorMsg.Contains("No Update could be performed, record not found", StringComparison.OrdinalIgnoreCase))
                    {
                        return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", "No record found with the given key.", logger);
                    }
                    else
                    {
                        // Unexpected error, rethrow to be handled by outer catch
                        throw;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 8) Normalize response (success or engine error payload)
                string rawPayloadJson = McpResponseBuilder.ExtractResultJson(mutationResult);
                using JsonDocument resultDoc = JsonDocument.Parse(rawPayloadJson);
                JsonElement root = resultDoc.RootElement;

                // Extract first item of value[] array (updated record)
                Dictionary<string, object?> filteredResult = new();
                if (root.TryGetProperty("value", out JsonElement valueArray) &&
                    valueArray.ValueKind == JsonValueKind.Array &&
                    valueArray.GetArrayLength() > 0)
                {
                    JsonElement firstItem = valueArray[0];
                    foreach (JsonProperty prop in firstItem.EnumerateObject())
                    {
                        filteredResult[prop.Name] = McpResponseBuilder.GetJsonValue(prop.Value);
                    }
                }

                return McpResponseBuilder.BuildSuccessResult(
                    new Dictionary<string, object?>
                    {
                        ["entity"] = entityName,
                        ["result"] = filteredResult,
                        ["message"] = $"Successfully updated record in entity '{entityName}'"
                    },
                    logger,
                    $"UpdateRecordTool success for entity {entityName}.");
            }
            catch (OperationCanceledException)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "OperationCanceled", "The update operation was canceled.", logger);
            }
            catch (ArgumentException argEx)
            {
                return McpResponseBuilder.BuildErrorResult(toolName, "InvalidArguments", argEx.Message, logger);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Unexpected error in UpdateRecordTool.");

                return McpResponseBuilder.BuildErrorResult(
                    toolName,
                    "UnexpectedError",
                    ex.Message ?? "An unexpected error occurred during the update operation.",
                    logger);
            }
        }
    }
}
