// Copyright (c) Microsoft.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.Model;
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
                Description = "Updates an existing record in the specified entity. Requires 'keys' to locate the record and 'fields' to specify new values.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""The name of the entity""
                            },
                            ""keys"": {
                                ""type"": ""object"",
                                ""description"": ""Key fields and their values to identify the record""
                            },
                            ""fields"": {
                                ""type"": ""object"",
                                ""description"": ""Fields and their new values to update""
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

            // 1) Resolve required services & configuration

            RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
            RuntimeConfig config = runtimeConfigProvider.GetConfig();

            // 2)Check if the tool is enabled in configuration before proceeding.
            if (config.McpDmlTools?.UpdateRecord != true)
            {
                return BuildErrorResult(
                    "ToolDisabled",
                    "The update_record tool is disabled in the configuration.",
                    logger);
            }

            try
            {

                cancellationToken.ThrowIfCancellationRequested();

                // 3) Parsing & basic argument validation (entity, keys, fields)
                if (arguments is null)
                {
                    return BuildErrorResult("InvalidArguments", "No arguments provided.", logger);
                }

                if (!TryParseArguments(arguments.RootElement, out string entityName, out Dictionary<string, object?> keys, out Dictionary<string, object?> fields, out string parseError))
                {
                    return BuildErrorResult("InvalidArguments", parseError, logger);
                }

                IMetadataProviderFactory metadataProviderFactory = serviceProvider.GetRequiredService<IMetadataProviderFactory>();
                IMutationEngineFactory mutationEngineFactory = serviceProvider.GetRequiredService<IMutationEngineFactory>();

                // 4) Resolve metadata for entity existence check
                string dataSourceName;
                ISqlMetadataProvider sqlMetadataProvider;

                try
                {
                    dataSourceName = config.GetDataSourceNameFromEntityName(entityName);
                    sqlMetadataProvider = metadataProviderFactory.GetMetadataProvider(dataSourceName);
                }
                catch (Exception)
                {
                    return BuildErrorResult("EntityNotFound", $"Entity '{entityName}' is not defined in the configuration.", logger);
                }

                if (!sqlMetadataProvider.EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? dbObject) || dbObject is null)
                {
                    return BuildErrorResult("EntityNotFound", $"Entity '{entityName}' is not defined in the configuration.", logger);
                }

                // 5) Authorization after we have a known entity
                IHttpContextAccessor httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                HttpContext? httpContext = httpContextAccessor.HttpContext;
                IAuthorizationResolver authResolver = serviceProvider.GetRequiredService<IAuthorizationResolver>();

                if (httpContext is null || !authResolver.IsValidRoleContext(httpContext))
                {
                    return BuildErrorResult("PermissionDenied", "Permission denied: unable to resolve a valid role context for update operation.", logger);
                }

                if (!TryResolveAuthorizedRoleHasPermission(httpContext, authResolver, entityName, out string? effectiveRole, out string authError))
                {
                    return BuildErrorResult("PermissionDenied", $"Permission denied: {authError}", logger);
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
                        return BuildErrorResult("InvalidArguments", $"Primary key value for '{kvp.Key}' cannot be null.", logger);
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
                        return BuildErrorResult(
                            "InvalidArguments",
                            "No record found with the given key.",
                            logger);
                    }
                    else
                    {
                        // Unexpected error, rethrow to be handled by outer catch
                        throw;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 8) Normalize response (success or engine error payload)
                string rawPayloadJson = ExtractResultJson(mutationResult);
                using JsonDocument resultDoc = JsonDocument.Parse(rawPayloadJson);
                JsonElement root = resultDoc.RootElement;

                return BuildSuccessResult(
                    entityName: entityName,
                    engineRootElement: root.Clone(),
                    logger: logger);
            }
            catch (OperationCanceledException)
            {
                return BuildErrorResult("OperationCanceled", "The update operation was canceled.", logger);
            }
            catch (ArgumentException argEx)
            {
                return BuildErrorResult("InvalidArguments", argEx.Message, logger);
            }
            catch (Exception ex)
            {
                ILogger<UpdateRecordTool>? innerLogger = serviceProvider.GetService<ILogger<UpdateRecordTool>>();
                innerLogger?.LogError(ex, "Unexpected error in UpdateRecordTool.");

                return BuildErrorResult(
                    "UnexpectedError",
                    ex.Message ?? "An unexpected error occurred during the update operation.",
                    logger);
            }
        }

        #region Parsing & Authorization

        private static bool TryParseArguments(
            JsonElement root,
            out string entityName,
            out Dictionary<string, object?> keys,
            out Dictionary<string, object?> fields,
            out string error)
        {
            entityName = string.Empty;
            keys = new Dictionary<string, object?>();
            fields = new Dictionary<string, object?>();
            error = string.Empty;

            if (!root.TryGetProperty("entity", out JsonElement entityEl) ||
                !root.TryGetProperty("keys", out JsonElement keysEl) ||
                !root.TryGetProperty("fields", out JsonElement fieldsEl))
            {
                error = "Missing required arguments 'entity', 'keys', or 'fields'.";
                return false;
            }

            // Parse and validate required arguments: entity, keys, fields
            entityName = entityEl.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entityName))
            {
                throw new ArgumentException("Entity is required", nameof(entityName));
            }

            if (keysEl.ValueKind != JsonValueKind.Object || fieldsEl.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("'keys' and 'fields' must be JSON objects.");
            }

            try
            {
                keys = JsonSerializer.Deserialize<Dictionary<string, object?>>(keysEl.GetRawText()) ?? new Dictionary<string, object?>();
                fields = JsonSerializer.Deserialize<Dictionary<string, object?>>(fieldsEl.GetRawText()) ?? new Dictionary<string, object?>();
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Failed to parse 'keys' or 'fields'", ex);
            }

            if (keys.Count == 0)
            {
                throw new ArgumentException("Keys are required to update an entity");
            }

            if (fields.Count == 0)
            {
                throw new ArgumentException("At least one field must be provided to update an entity", nameof(fields));
            }

            foreach (KeyValuePair<string, object?> kv in keys)
            {
                if (kv.Value is null || (kv.Value is string str && string.IsNullOrWhiteSpace(str)))
                {
                    throw new ArgumentException($"Key value for '{kv.Key}' cannot be null or empty.");
                }
            }

            return true;
        }

        private static bool TryResolveAuthorizedRoleHasPermission(
            HttpContext httpContext,
            IAuthorizationResolver authorizationResolver,
            string entityName,
            out string? effectiveRole,
            out string error)
        {
            effectiveRole = null;
            error = string.Empty;

            string roleHeader = httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();

            if (string.IsNullOrWhiteSpace(roleHeader))
            {
                error = "Client role header is missing or empty.";
                return false;
            }

            string[] roles = roleHeader
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (roles.Length == 0)
            {
                error = "Client role header is missing or empty.";
                return false;
            }

            foreach (string role in roles)
            {
                bool allowed = authorizationResolver.AreRoleAndOperationDefinedForEntity(
                    entityName, role, EntityActionOperation.Update);

                if (allowed)
                {
                    effectiveRole = role;
                    return true;
                }
            }

            error = "You do not have permission to update records for this entity.";
            return false;
        }

        #endregion

        #region Response Builders & Utilities

        private static CallToolResult BuildSuccessResult(
            string entityName,
            JsonElement engineRootElement,
            ILogger? logger)
        {
            // Extract only requested keys and updated fields from engineRootElement
            Dictionary<string, object?> filteredResult = new();

            // Navigate to "value" array in the engine result
            if (engineRootElement.TryGetProperty("value", out JsonElement valueArray) &&
                valueArray.ValueKind == JsonValueKind.Array &&
                valueArray.GetArrayLength() > 0)
            {
                JsonElement firstItem = valueArray[0];

                // Include all properties from the result
                foreach (JsonProperty prop in firstItem.EnumerateObject())
                {
                    filteredResult[prop.Name] = GetJsonValue(prop.Value);
                }
            }

            // Build normalized response
            Dictionary<string, object?> normalized = new()
            {
                ["status"] = "success",
                ["result"] = filteredResult
            };

            string output = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });

            logger?.LogInformation("UpdateRecordTool success for entity {Entity}.", entityName);

            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Type = "text", Text = output }
                }
            };
        }

        /// <summary>
        /// Converts JsonElement to .NET object dynamically.
        /// </summary>
        private static object? GetJsonValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText() // fallback for arrays/objects
            };
        }

        private static CallToolResult BuildErrorResult(
            string errorType,
            string message,
            ILogger? logger)
        {
            Dictionary<string, object?> errorObj = new()
            {
                ["status"] = "error",
                ["error"] = new Dictionary<string, object?>
                {
                    ["type"] = errorType,
                    ["message"] = message
                }
            };

            string output = JsonSerializer.Serialize(errorObj);

            logger?.LogWarning("UpdateRecordTool error {ErrorType}: {Message}", errorType, message);

            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock { Type = "text", Text = output }
                ],
                IsError = true
            };
        }

        /// <summary>
        /// Extracts a JSON string from a typical IActionResult.
        /// Falls back to "{}" for unsupported/empty cases to avoid leaking internals.
        /// </summary>
        private static string ExtractResultJson(IActionResult? result)
        {
            switch (result)
            {
                case ObjectResult obj:
                    if (obj.Value is JsonElement je)
                    {
                        return je.GetRawText();
                    }

                    if (obj.Value is JsonDocument jd)
                    {
                        return jd.RootElement.GetRawText();
                    }

                    return JsonSerializer.Serialize(obj.Value ?? new object());

                case ContentResult content:
                    return string.IsNullOrWhiteSpace(content.Content) ? "{}" : content.Content;

                default:
                    return "{}";
            }
        }

        #endregion
    }
}
