// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.CustomTools
{
    /// <summary>
    /// Custom tool for executing stored procedures configured in DAB
    /// </summary>
    public class ExecuteStoredProcedureTool : IMcpTool
    {
        public ToolType ToolType { get; } = ToolType.Custom;

        McpEnums.ToolType IMcpTool.ToolType => throw new NotImplementedException();

        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "execute_stored_procedure",
                Description = "Executes a stored procedure configured in DAB and returns the results",
                InputSchema = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        entityName = new
                        {
                            type = "string",
                            description = "Name of the stored procedure entity as configured in dab-config"
                        },
                        parameters = new
                        {
                            type = "object",
                            description = "Parameters to pass to the stored procedure as key-value pairs",
                            additionalProperties = true
                        }
                    },
                    required = new[] { "entityName" }
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

                // Extract entity name
                if (!arguments.RootElement.TryGetProperty("entityName", out var entityNameElement) ||
                    entityNameElement.ValueKind != JsonValueKind.String)
                {
                    return CreateErrorResult("Missing or invalid 'entityName' parameter");
                }

                string entityName = entityNameElement.GetString()!;

                // Get runtime configuration
                var runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                var runtimeConfig = runtimeConfigProvider.GetConfig();

                // Validate entity exists
                if (!runtimeConfig.Entities.TryGetValue(entityName, out var entity))
                {
                    return CreateErrorResult($"Entity '{entityName}' not found in configuration");
                }

                // Validate it's a stored procedure
                if (entity.Source.Type != EntitySourceType.StoredProcedure)
                {
                    return CreateErrorResult($"Entity '{entityName}' is not a stored procedure");
                }

                // Build parameters
                Dictionary<string, object?> parameters = new();
                
                // Add default parameters from configuration
                if (entity.Source.Parameters != null)
                {
                    foreach (var param in entity.Source.Parameters)
                    {
                        parameters[param.Key] = param.Value;
                    }
                }
                
                // Override with runtime parameters
                if (arguments.RootElement.TryGetProperty("parameters", out var parametersElement) &&
                    parametersElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in parametersElement.EnumerateObject())
                    {
                        parameters[property.Name] = GetParameterValue(property.Value);
                    }
                }

                // Get stored procedure name from entity configuration
                string storedProcedureName = entity.Source.Object!;

                // Get the database connection string
                string dataSourceName = runtimeConfig.DefaultDataSourceName;
                var dataSource = runtimeConfig.GetDataSourceFromDataSourceName(dataSourceName);
                string connectionString = dataSource.ConnectionString;

                // Execute stored procedure directly
                var results = new List<Dictionary<string, object?>>();
                
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    
                    using (var command = new SqlCommand(storedProcedureName, connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        
                        // Add parameters
                        foreach (var param in parameters)
                        {
                            command.Parameters.AddWithValue($"@{param.Key}", param.Value ?? DBNull.Value);
                        }
                        
                        // Execute and read results
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var row = new Dictionary<string, object?>();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    string columnName = reader.GetName(i);
                                    object? value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                    row[columnName] = value;
                                }
                                results.Add(row);
                            }
                        }
                    }
                }

                // Format the response
                var response = new
                {
                    success = true,
                    entity = entityName,
                    storedProcedure = storedProcedureName,
                    rowCount = results.Count,
                    data = results
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
                return CreateErrorResult($"DAB Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Unexpected error: {ex.Message}");
            }
        }

        private static object? GetParameterValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt32(out var intValue) ? intValue : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        private static CallToolResult CreateErrorResult(string errorMessage)
        {
            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock
                    {
                        Type = "text",
                        Text = JsonSerializer.Serialize(new { error = errorMessage })
                    }
                },
                IsError = true
            };
        }
    }
}
