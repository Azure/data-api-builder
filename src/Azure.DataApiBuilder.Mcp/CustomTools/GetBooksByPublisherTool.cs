// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.CustomTools
{

    /// <summary>
    /// Custom tool for retrieving books by publisher using a stored procedure configured in DAB
    /// </summary>
    public class GetBooksByPublisherTool : IMcpTool
    {
        public ToolType ToolType { get; } = ToolType.Custom;

        McpEnums.ToolType IMcpTool.ToolType => throw new NotImplementedException();

        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "get_books_by_publisher",
                Description = "Retrieves books published by a specific publisher using a stored procedure",
                InputSchema = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        publisher = new
                        {
                            type = "string",
                            description = "Name of the publisher to search for"
                        },
                        includeOutOfStock = new
                        {
                            type = "boolean",
                            description = "Whether to include out of stock books",
                            @default = false
                        }
                    },
                    required = new[] { "publisher" }
                })
            };
        }

        public async Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (arguments?.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return CreateErrorResult("Invalid arguments: expected object");
                }

                // Extract publisher parameter
                if (!arguments.RootElement.TryGetProperty("publisher", out var publisherElement) ||
                    publisherElement.ValueKind != JsonValueKind.String)
                {
                    return CreateErrorResult("Missing or invalid 'publisher' parameter");
                }

                string publisher = publisherElement.GetString()!;
                
                // Extract optional includeOutOfStock parameter
                bool includeOutOfStock = false;
                if (arguments.RootElement.TryGetProperty("includeOutOfStock", out var includeOutOfStockElement))
                {
                    includeOutOfStock = includeOutOfStockElement.GetBoolean();
                }

                // Define the entity name as configured in dab-config.json
                const string entityName = "GetBooksByPublisher";

                // Get required services
                var runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                var runtimeConfig = runtimeConfigProvider.GetConfig();

                // Validate entity exists
                if (!runtimeConfig.Entities.TryGetValue(entityName, out var entity))
                {
                    return CreateErrorResult($"Stored procedure entity '{entityName}' not found in configuration");
                }

                // Set up HTTP context with parameters for the stored procedure
                var httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                if (httpContextAccessor.HttpContext != null)
                {
                    // Create request body with parameters
                    var requestBody = new Dictionary<string, object?>
                    {
                        { "publisher", publisher },
                        { "includeOutOfStock", includeOutOfStock }
                    };
                    
                    var json = JsonSerializer.Serialize(requestBody);
                    var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
                    httpContextAccessor.HttpContext.Request.Body = stream;
                    httpContextAccessor.HttpContext.Request.ContentType = "application/json";
                    httpContextAccessor.HttpContext.Request.ContentLength = stream.Length;
                }

                // Use RestService to execute the stored procedure
                var restService = serviceProvider.GetRequiredService<RestService>();
                var result = await restService.ExecuteAsync(entityName, EntityActionOperation.Execute, primaryKeyRoute: null);

                if (result == null)
                {
                    var noDataResponse = new 
                    { 
                        success = true,
                        message = "No books found for the specified publisher",
                        data = Array.Empty<object>()
                    };
                    
                    return new CallToolResult
                    {
                        Content = new List<ContentBlock>
                        {
                            new TextContentBlock
                            {
                                Type = "text",
                                Text = JsonSerializer.Serialize(noDataResponse)
                            }
                        }
                    };
                }

                // Extract the response data
                object? responseData = null;
                if (result is ObjectResult objectResult)
                {
                    responseData = objectResult.Value;
                }
                else if (result is JsonResult jsonResult)
                {
                    responseData = jsonResult.Value;
                }

                var response = new
                {
                    success = true,
                    publisher = publisher,
                    includeOutOfStock = includeOutOfStock,
                    data = responseData
                };

                return new CallToolResult
                {
                    Content = new List<ContentBlock>
                    {
                        new TextContentBlock
                        {
                            Type = "text",
                            Text = JsonSerializer.Serialize(response, new JsonSerializerOptions 
                            { 
                                WriteIndented = true 
                            })
                        }
                    }
                };
            }
            catch (DataApiBuilderException ex)
            {
                return CreateErrorResult($"Database operation failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Unexpected error: {ex.Message}");
            }
        }

        private static CallToolResult CreateErrorResult(string errorMessage)
        {
            var errorResponse = new 
            { 
                error = errorMessage,
                success = false
            };
            
            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock
                    {
                        Type = "text",
                        Text = JsonSerializer.Serialize(errorResponse)
                    }
                },
                IsError = true
            };
        }
    }
}
