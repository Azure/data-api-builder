// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Mcp.Tools
{
    public class EchoTool : IMcpTool
    {
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
