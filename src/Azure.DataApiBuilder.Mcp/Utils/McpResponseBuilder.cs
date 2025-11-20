// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Mcp.Utils
{
    /// <summary>
    /// Utility class for building standardized MCP tool responses.
    /// </summary>
    public static class McpResponseBuilder
    {
        /// <summary>
        /// Builds a success response for MCP tools.
        /// </summary>
        public static CallToolResult BuildSuccessResult(
            Dictionary<string, object?> responseData,
            ILogger? logger = null,
            string? logMessage = null)
        {
            responseData["status"] = "success";

            string output = JsonSerializer.Serialize(responseData, new JsonSerializerOptions { WriteIndented = true });

            if (logger != null && !string.IsNullOrEmpty(logMessage))
            {
                logger.LogInformation(logMessage);
            }

            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Type = "text", Text = output }
                }
            };
        }

        /// <summary>
        /// Builds an error response for MCP tools.
        /// </summary>
        public static CallToolResult BuildErrorResult(
            string toolName,
            string errorType,
            string message,
            ILogger? logger = null)
        {
            Dictionary<string, object?> errorObj = new()
            {
                ["toolName"] = toolName,
                ["status"] = "error",
                ["error"] = new Dictionary<string, object?>
                {
                    ["type"] = errorType,
                    ["message"] = message
                }
            };

            string output = JsonSerializer.Serialize(errorObj, new JsonSerializerOptions { WriteIndented = true });

            logger?.LogWarning("MCP Tool error {ErrorType}: {Message}", errorType, message);

            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Type = "text", Text = output }
                },
                IsError = true
            };
        }

        /// <summary>
        /// Extracts a JSON string from a typical IActionResult.
        /// Falls back to "{}" for unsupported/empty cases to avoid leaking internals.
        /// </summary>
        public static string ExtractResultJson(IActionResult? result)
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
    }
}
