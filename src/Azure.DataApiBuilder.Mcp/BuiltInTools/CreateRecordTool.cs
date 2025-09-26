// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Model;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.BuiltInTools
{
    public class CreateRecordTool : IMcpTool
    {
        public ToolType ToolType { get; } = ToolType.BuiltIn;

        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "create-record",
                Description = "Creates a new record in the specified entity.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""entity"": {
                                ""type"": ""string"",
                                ""description"": ""The name of the entity""
                            },
                            ""data"": {
                                ""type"": ""object"",
                                ""description"": ""The data for the new record""
                            }
                        },
                        ""required"": [""entity"", ""data""]
                    }"
                )
            };
        }

        public Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            if (arguments == null)
            {
                return Task.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Type = "text", Text = "Error: No arguments provided" }]
                });
            }

            try
            {
                // Extract arguments
                JsonElement root = arguments.RootElement;

                if (!root.TryGetProperty("entity", out JsonElement entityElement) ||
                    !root.TryGetProperty("data", out JsonElement dataElement))
                {
                    return Task.FromResult(new CallToolResult
                    {
                        Content = [new TextContentBlock { Type = "text", Text = "Error: Missing required arguments 'entity' or 'data'" }]
                    });
                }

                string entityName = entityElement.GetString() ?? string.Empty;

                // TODO: Implement actual create logic using DAB's internal services
                // For now, return a placeholder response
                string result = $"Would create record in entity '{entityName}' with data: {dataElement.GetRawText()}";

                return Task.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Type = "text", Text = result }]
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Type = "text", Text = $"Error: {ex.Message}" }]
                });
            }
        }
    }
}
