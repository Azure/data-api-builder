// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Model;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.BuiltInTools
{
    public class EchoTool : IMcpTool
    {
        public ToolType ToolType { get; } = ToolType.BuiltIn;

        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "echonew",
                Description = "Echoes the input back to the client.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": { ""message"": { ""type"": ""string"" } },
                        ""required"": [""message""]
                    }"
                )
            };
        }

        public Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            string? message = null;

            if (arguments?.RootElement.TryGetProperty("message", out JsonElement messageEl) == true)
            {
                message = messageEl.ValueKind == JsonValueKind.String
                    ? messageEl.GetString()
                    : messageEl.ToString();
            }

            return Task.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Type = "text", Text = $"Echo: {message}" }]
            });
        }
    }
}
