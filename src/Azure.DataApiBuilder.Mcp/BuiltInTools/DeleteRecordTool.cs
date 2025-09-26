// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.BuiltInTools
{
    /// <summary>
    /// Tool to delete records from a table/view entity configured in DAB.
    /// </summary>
    public class DeleteRecordTool : IMcpTool
    {
        public ToolType ToolType { get; } = ToolType.BuiltIn;

        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "delete-record",
                Description = "Deletes records from a table or view based on specified conditions",
                InputSchema = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        entityName = new
                        {
                            type = "string",
                            description = "Name of the entity (table/view) as configured in dab-config"
                        },
                        primaryKey = new
                        {
                            type = "object",
                            description = "Primary key values to identify the record(s) to delete",
                            additionalProperties = true
                        },
                        filter = new
                        {
                            type = "string",
                            description = "Optional WHERE clause conditions (e.g., 'age > 18 AND status = \"active\"')"
                        }
                    },
                    required = new[] { "entityName" },
                    oneOf = new[]
                    {
                        new { required = new[] { "primaryKey" } },
                        new { required = new[] { "filter" } }
                    }
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
                if (!arguments.RootElement.TryGetProperty("entityName", out JsonElement entityNameElement) ||
                    entityNameElement.ValueKind != JsonValueKind.String)
                {
                    return CreateErrorResult("Missing or invalid 'entityName' parameter");
                }

                string entityName = entityNameElement.GetString()!;

                // Get runtime configuration
                RuntimeConfigProvider runtimeConfigProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();

                // Validate entity exists
                if (!runtimeConfig.Entities.TryGetValue(entityName, out Entity? entity))
                {
                    return CreateErrorResult($"Entity '{entityName}' not found in configuration");
                }

                // Validate it's a table or view
                if (entity.Source.Type != EntitySourceType.Table && entity.Source.Type != EntitySourceType.View)
                {
                    return CreateErrorResult($"Entity '{entityName}' is not a table or view. Use 'execute-entity' for stored procedures.");
                }

                // Check permissions for delete action
                bool hasDeletePermission = entity.Permissions?.Any(p =>
                    p.Actions?.Any(a => a.Action == EntityActionOperation.Delete) == true) == true;

                if (!hasDeletePermission)
                {
                    return CreateErrorResult($"Delete operation is not permitted for entity '{entityName}'");
                }

                // Get table/view name from entity configuration
                string tableName = entity.Source.Object!;

                // Build WHERE clause
                string whereClause;
                Dictionary<string, object?> parameters = new();

                bool hasPrimaryKey = arguments.RootElement.TryGetProperty("primaryKey", out JsonElement primaryKeyElement) &&
                                     primaryKeyElement.ValueKind == JsonValueKind.Object;
                bool hasFilter = arguments.RootElement.TryGetProperty("filter", out JsonElement filterElement) &&
                                filterElement.ValueKind == JsonValueKind.String;

                if (!hasPrimaryKey && !hasFilter)
                {
                    return CreateErrorResult("Either 'primaryKey' or 'filter' must be provided");
                }

                if (hasPrimaryKey && hasFilter)
                {
                    return CreateErrorResult("Cannot specify both 'primaryKey' and 'filter'. Use one or the other.");
                }

                if (hasPrimaryKey)
                {
                    // Build WHERE clause from primary key
                    List<string> conditions = new();
                    foreach (JsonProperty prop in primaryKeyElement.EnumerateObject())
                    {
                        string paramName = $"pk_{prop.Name}";
                        conditions.Add($"[{prop.Name}] = @{paramName}");
                        parameters[paramName] = GetParameterValue(prop.Value);
                    }

                    whereClause = string.Join(" AND ", conditions);
                }
                else
                {
                    // Use the provided filter
                    whereClause = filterElement.GetString()!;

                    // Basic SQL injection prevention - check for dangerous patterns
                    string[] dangerousPatterns = { "--", "/*", "*/", "xp_", "sp_", "exec", "execute", "drop", "create", "alter" };
                    string filterLower = whereClause.ToLower();
                    foreach (string pattern in dangerousPatterns)
                    {
                        if (filterLower.Contains(pattern))
                        {
                            return CreateErrorResult($"Filter contains potentially dangerous SQL pattern: '{pattern}'");
                        }
                    }
                }

                // Get the database connection string
                string dataSourceName = runtimeConfig.DefaultDataSourceName;
                DataSource? dataSource = runtimeConfig.GetDataSourceFromDataSourceName(dataSourceName);
                string connectionString = dataSource.ConnectionString;

                // Execute delete operation
                int rowsAffected = 0;
                List<Dictionary<string, object?>> deletedRecords = new();

                using (SqlConnection connection = new(connectionString))
                {
                    await connection.OpenAsync(cancellationToken);

                    // First, get a preview of what will be deleted (for response)
                    string selectQuery = $"SELECT * FROM [{tableName}] WHERE {whereClause}";

                    using (SqlCommand selectCommand = new(selectQuery, connection))
                    {
                        // Add parameters if using primary key
                        foreach (KeyValuePair<string, object?> param in parameters)
                        {
                            selectCommand.Parameters.AddWithValue($"@{param.Key}", param.Value ?? DBNull.Value);
                        }

                        using (SqlDataReader reader = await selectCommand.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                Dictionary<string, object?> row = new();

                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    string columnName = reader.GetName(i);
                                    object? value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                    row[columnName] = value;
                                }

                                deletedRecords.Add(row);
                            }
                        }
                    }

                    // Now execute the delete
                    string deleteQuery = $"DELETE FROM [{tableName}] WHERE {whereClause}";

                    using (SqlCommand deleteCommand = new(deleteQuery, connection))
                    {
                        // Add parameters if using primary key
                        foreach (KeyValuePair<string, object?> param in parameters)
                        {
                            deleteCommand.Parameters.AddWithValue($"@{param.Key}", param.Value ?? DBNull.Value);
                        }

                        rowsAffected = await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                // Format the response
                var response = new
                {
                    success = true,
                    entity = entityName,
                    table = tableName,
                    rowsDeleted = rowsAffected,
                    deletedRecords = deletedRecords
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
                                WriteIndented = true,
                                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                            })
                        }
                    }
                };
            }
            catch (SqlException ex)
            {
                return CreateErrorResult($"Database error: {ex.Message}");
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
                JsonValueKind.Number => element.TryGetInt32(out int intValue) ? intValue : element.GetDouble(),
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
