// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text.Json;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;

using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
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
        public Tool GetToolMetadata() => new()
        {
            Name = "delete_record",
            Description = "STEP 1: describe_entities -> find entities with DELETE permission and their key fields. STEP 2: call this tool with full key values.",
            InputSchema = JsonSerializer.Deserialize<JsonElement>(
                @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""Entity name with DELETE permission.""
                            },
                            ""keys"": {
                                ""type"": ""object"",
                                ""description"": ""All key fields identifying the record.""
                            }
                        },
                        ""required"": [""entity"", ""keys""]
                    }")
        };

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
                // Cancellation check at the start
                cancellationToken.ThrowIfCancellationRequested();

                // 1) Resolve required services & configuration
                RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig config = runtimeConfigProvider.GetConfig();

                // 2) Check if the tool is enabled in configuration before proceeding
                if (config.McpDmlTools?.DeleteRecord != true)
                {
                    return McpResponseBuilder.BuildErrorResult(
                        "ToolDisabled",
                        $"The {GetToolMetadata().Name} tool is disabled in the configuration.",
                        logger);
                }

                // 3) Parsing & basic argument validation
                if (arguments is null)
                {
                    return McpResponseBuilder.BuildErrorResult("InvalidArguments", "No arguments provided.", logger);
                }

                if (!McpArgumentParser.TryParseEntityAndKeys(arguments.RootElement, out string entityName, out Dictionary<string, object?> keys, out string parseError))
                {
                    return McpResponseBuilder.BuildErrorResult("InvalidArguments", parseError, logger);
                }

                // 4) Resolve metadata for entity existence check
                string dataSourceName;
                Azure.DataApiBuilder.Core.Services.ISqlMetadataProvider sqlMetadataProvider;

                if (!McpMetadataHelper.TryResolveMetadata(
                        entityName,
                        config,
                        serviceProvider,
                        out sqlMetadataProvider,
                        out DatabaseObject dbObject,
                        out dataSourceName,
                        out string metadataError))
                {
                    return McpResponseBuilder.BuildErrorResult("EntityNotFound", metadataError, logger);
                }

                // Validate it's a table or view
                if (dbObject.SourceType != EntitySourceType.Table && dbObject.SourceType != EntitySourceType.View)
                {
                    return McpResponseBuilder.BuildErrorResult("InvalidEntity", $"Entity '{entityName}' is not a table or view. Use 'execute-entity' for stored procedures.", logger);
                }

                // 5) Authorization
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

                // Need MetadataProviderFactory for RequestValidator; resolve here.
                var metadataProviderFactory = serviceProvider.GetRequiredService<Azure.DataApiBuilder.Core.Services.MetadataProviders.IMetadataProviderFactory>();
                Azure.DataApiBuilder.Core.Services.RequestValidator requestValidator = new(metadataProviderFactory, runtimeConfigProvider);

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

                var mutationEngineFactory = serviceProvider.GetRequiredService<Azure.DataApiBuilder.Core.Resolvers.Factories.IMutationEngineFactory>();
                DatabaseType dbType = config.GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;
                IMutationEngine mutationEngine = mutationEngineFactory.GetMutationEngine(dbType);

                IActionResult? mutationResult = null;
                try
                {
                    // Cancellation check before executing
                    cancellationToken.ThrowIfCancellationRequested();
                    mutationResult = await mutationEngine.ExecuteAsync(context).ConfigureAwait(false);
                }
                catch (DataApiBuilderException dabEx)
                {
                    // Handle specific DAB exceptions
                    logger?.LogError(dabEx, "Data API Builder error deleting record from {Entity}", entityName);

                    string message = dabEx.Message;

                    // Check for specific error patterns
                    if (message.Contains("Could not find item with", StringComparison.OrdinalIgnoreCase))
                    {
                        string keyDetails = McpJsonHelper.FormatKeyDetails(keys);
                        return McpResponseBuilder.BuildErrorResult(
                            "RecordNotFound",
                            $"No record found with the specified primary key: {keyDetails}",
                            logger);
                    }
                    else if (message.Contains("violates foreign key constraint", StringComparison.OrdinalIgnoreCase) ||
                             message.Contains("REFERENCE constraint", StringComparison.OrdinalIgnoreCase))
                    {
                        return McpResponseBuilder.BuildErrorResult(
                            "ConstraintViolation",
                            "Cannot delete record due to foreign key constraint. Other records depend on this record.",
                            logger);
                    }
                    else if (message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
                             message.Contains("authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        return McpResponseBuilder.BuildErrorResult(
                            "PermissionDenied",
                            "You do not have permission to delete this record.",
                            logger);
                    }
                    else if (message.Contains("invalid", StringComparison.OrdinalIgnoreCase) &&
                             message.Contains("type", StringComparison.OrdinalIgnoreCase))
                    {
                        return McpResponseBuilder.BuildErrorResult(
                            "InvalidArguments",
                            "Invalid data type for one or more key values.",
                            logger);
                    }

                    // For any other DAB exceptions, return the message as-is
                    return McpResponseBuilder.BuildErrorResult(
                        "DataApiBuilderError",
                        dabEx.Message,
                        logger);
                }
                catch (SqlException sqlEx)
                {
                    // Handle SQL Server specific errors
                    logger?.LogError(sqlEx, "SQL Server error deleting record from {Entity}", entityName);
                    string errorMessage = sqlEx.Number switch
                    {
                        547 => "Cannot delete record due to foreign key constraint. Other records depend on this record.",
                        2627 or 2601 => "Cannot delete record due to unique constraint violation.",
                        229 or 262 => $"Permission denied to delete from table '{dbObject.FullName}'.",
                        208 => $"Table '{dbObject.FullName}' not found in the database.",
                        _ => $"Database error: {sqlEx.Message}"
                    };
                    return McpResponseBuilder.BuildErrorResult("DatabaseError", errorMessage, logger);
                }
                catch (DbException dbEx)
                {
                    // Handle generic database exceptions (works for PostgreSQL, MySQL, etc.)
                    logger?.LogError(dbEx, "Database error deleting record from {Entity}", entityName);

                    // Check for common patterns in error messages
                    string errorMsg = dbEx.Message.ToLowerInvariant();
                    if (errorMsg.Contains("foreign key") || errorMsg.Contains("constraint"))
                    {
                        return McpResponseBuilder.BuildErrorResult(
                            "ConstraintViolation",
                            "Cannot delete record due to foreign key constraint. Other records depend on this record.",
                            logger);
                    }
                    else if (errorMsg.Contains("not found") || errorMsg.Contains("does not exist"))
                    {
                        return McpResponseBuilder.BuildErrorResult(
                            "RecordNotFound",
                            "No record found with the specified primary key.",
                            logger);
                    }

                    return McpResponseBuilder.BuildErrorResult("DatabaseError", $"Database error: {dbEx.Message}", logger);
                }
                catch (InvalidOperationException ioEx) when (ioEx.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle connection-related issues
                    logger?.LogError(ioEx, "Database connection error");
                    return McpResponseBuilder.BuildErrorResult("ConnectionError", "Failed to connect to the database.", logger);
                }
                catch (TimeoutException timeoutEx)
                {
                    // Handle query timeout
                    logger?.LogError(timeoutEx, "Delete operation timeout for {Entity}", entityName);
                    return McpResponseBuilder.BuildErrorResult("TimeoutError", "The delete operation timed out.", logger);
                }
                catch (Exception ex)
                {
                    string errorMsg = ex.Message ?? string.Empty;

                    if (errorMsg.Contains("Could not find", StringComparison.OrdinalIgnoreCase) ||
                        errorMsg.Contains("record not found", StringComparison.OrdinalIgnoreCase))
                    {
                        string keyDetails = McpJsonHelper.FormatKeyDetails(keys);
                        return McpResponseBuilder.BuildErrorResult(
                            "RecordNotFound",
                            $"No entity found with the given key {keyDetails}.",
                            logger);
                    }
                    else
                    {
                        // Re-throw unexpected exceptions
                        throw;
                    }
                }

                // 8) Build response
                // Based on SqlMutationEngine, delete operations typically return NoContentResult
                // We build a success response with just the operation details
                Dictionary<string, object?> responseData = new()
                {
                    ["entity"] = entityName,
                    ["keyDetails"] = McpJsonHelper.FormatKeyDetails(keys),
                    ["message"] = "Record deleted successfully"
                };

                // If the mutation result is OkObjectResult (which would be unusual for delete),
                // include the result value directly without re-serialization
                if (mutationResult is OkObjectResult okObjectResult && okObjectResult.Value is not null)
                {
                    responseData["result"] = okObjectResult.Value;
                }

                return McpResponseBuilder.BuildSuccessResult(
                    responseData,
                    logger,
                    $"DeleteRecordTool success for entity {entityName}."
                );
            }
            catch (OperationCanceledException)
            {
                return McpResponseBuilder.BuildErrorResult("OperationCanceled", "The delete operation was canceled.", logger);
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
                    "An unexpected error occurred during the delete operation.",
                    logger);
            }
        }
    }
}
