// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Model;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.Enums;

namespace Azure.DataApiBuilder.Mcp.BuiltInTools
{
    /* This is a sample for reference and will be deleted
    // to call the tool, use the following JSON payload body and make POST request to: http://localhost:5000/mcp

    {
      "jsonrpc": "2.0",
      "id": 1,
      "method": "tools/call",
      "params": {
        "name": "process_data",
        "arguments": {
          "name": "DataOperation",
          "count": 42,
          "enabled": true,
          "threshold": 3.14,
          "tags": ["tag1", "tag2", "tag3"],
          "options": {
            "verbose": true,
            "timeout": 60
          }
        }
      }
    }
     
    */
    public class ComplexTool : IMcpTool
    {
        public ToolType ToolType { get; } = ToolType.BuiltIn;

        public Tool GetToolMetadata()
        {
            return new Tool
            {
                Name = "process_data",
                Description = "Processes data with multiple parameters of different types.",
                InputSchema = JsonSerializer.Deserialize<JsonElement>(
                    @"{
                        ""type"": ""object"",
                        ""properties"": {
                            ""name"": { ""type"": ""string"", ""description"": ""Name of the operation"" },
                            ""count"": { ""type"": ""integer"", ""description"": ""Number of items to process"" },
                            ""enabled"": { ""type"": ""boolean"", ""description"": ""Whether processing is enabled"" },
                            ""threshold"": { ""type"": ""number"", ""description"": ""Processing threshold value"" },
                            ""tags"": {
                                ""type"": ""array"",
                                ""items"": { ""type"": ""string"" },
                                ""description"": ""List of tags""
                            },
                            ""options"": {
                                ""type"": ""object"",
                                ""properties"": {
                                    ""verbose"": { ""type"": ""boolean"" },
                                    ""timeout"": { ""type"": ""integer"" }
                                }
                            }
                        },
                        ""required"": [""name"", ""count""]
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

            JsonElement root = arguments.RootElement;

            // Extract different types of parameters
            string name = root.TryGetProperty("name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString() ?? "Unknown"
                : "Unknown";

            int count = root.TryGetProperty("count", out JsonElement countEl) && countEl.ValueKind == JsonValueKind.Number
                ? countEl.GetInt32()
                : 0;

            bool enabled = root.TryGetProperty("enabled", out JsonElement enabledEl) && enabledEl.ValueKind == JsonValueKind.True;

            double threshold = root.TryGetProperty("threshold", out JsonElement thresholdEl) && thresholdEl.ValueKind == JsonValueKind.Number
                ? thresholdEl.GetDouble()
                : 0.0;

            List<string> tags = new();
            if (root.TryGetProperty("tags", out JsonElement tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement tag in tagsEl.EnumerateArray())
                {
                    if (tag.ValueKind == JsonValueKind.String)
                    {
                        tags.Add(tag.GetString() ?? string.Empty);
                    }
                }
            }

            bool verbose = false;
            int timeout = 30;
            if (root.TryGetProperty("options", out JsonElement optionsEl) && optionsEl.ValueKind == JsonValueKind.Object)
            {
                verbose = optionsEl.TryGetProperty("verbose", out JsonElement verboseEl) && verboseEl.ValueKind == JsonValueKind.True;

                if (optionsEl.TryGetProperty("timeout", out JsonElement timeoutEl) && timeoutEl.ValueKind == JsonValueKind.Number)
                {
                    timeout = timeoutEl.GetInt32();
                }
            }

            // Process the data (example logic)
            string result = $"Processed: name={name}, count={count}, enabled={enabled}, threshold={threshold}, " +
                          $"tags=[{string.Join(", ", tags)}], verbose={verbose}, timeout={timeout}";

            return Task.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Type = "text", Text = result }]
            });
        }
    }
}
