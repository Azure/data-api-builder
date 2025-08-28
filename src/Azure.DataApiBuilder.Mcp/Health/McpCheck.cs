// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ModelContextProtocol.Client;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Mcp.Health;

public class McpCheck
{
    private readonly static string _serviceRegistrationCheckName = "MCP Server Tools - Service Registration";
    private readonly static string _clientConnectionCheckName = "MCP Server Tools - Client Connection";

    /// <summary>
    /// Performs comprehensive MCP health checks including both service registration and client connection
    /// </summary>
    public static async Task<CheckResult[]> CheckAllAsync(IServiceProvider serviceProvider)
    {
        CheckResult serviceRegistrationCheck = CheckServiceRegistration(serviceProvider);
        CheckResult clientConnectionCheck = await CheckClientConnectionAsync(serviceProvider);

        // testing
        string x = await Tools.DmlTools.InternalGetGraphQLSchemaAsync(serviceProvider);
        Console.WriteLine(x);   

        return new[] { serviceRegistrationCheck, clientConnectionCheck };
    }

    /// <summary>
    /// Checks if MCP tools are properly registered in the service provider
    /// </summary>
    public static CheckResult CheckServiceRegistration(IServiceProvider serviceProvider)
    {
        try
        {
            ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            ILogger logger = loggerFactory.CreateLogger<McpCheck>();

            logger.LogInformation("Checking MCP server tools service registration");

            // Check if MCP tools are registered in the service provider
            IEnumerable<McpServerTool> mcpTools = serviceProvider.GetServices<McpServerTool>();
            string[] toolNames = mcpTools
                .Select(t => t.ProtocolTool.Name)
                .OrderBy(t => t).ToArray();

            logger.LogInformation("Found {ToolCount} registered MCP tools in services: {Tools}", toolNames.Length, string.Join(", ", toolNames));

            return new CheckResult(
                Name: _serviceRegistrationCheckName,
                IsHealthy: toolNames.Length != 0,
                Message: toolNames.Length != 0 ? "Tools registered in services" : "No tools registered in services",
                Tags: new Dictionary<string, string>
                {
                    { "check_type", "service_registration" },
                    { "tools", string.Join(", ", toolNames) },
                    { "tool_count", toolNames.Length.ToString() }
                });
        }
        catch (Exception ex)
        {
            return new CheckResult(
                Name: _serviceRegistrationCheckName,
                IsHealthy: false,
                Message: $"Service registration check failed: {ex.Message}",
                Tags: new Dictionary<string, string>
                {
                    { "check_type", "service_registration" },
                    { "error_type", "general_error" },
                    { "error_message", ex.Message }
                }
            );
        }
    }

    /// <summary>
    /// Checks if MCP client can successfully connect and list tools from the MCP server
    /// </summary>
    public static async Task<CheckResult> CheckClientConnectionAsync(IServiceProvider serviceProvider)
    {
        try
        {
            ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            ILogger logger = loggerFactory.CreateLogger<McpCheck>();

            // Get the MCP server endpoint
            HttpRequest? request = serviceProvider.GetService<IHttpContextAccessor>()?.HttpContext?.Request;
            if (request == null)
            {
                return new CheckResult(
                    Name: _clientConnectionCheckName,
                    IsHealthy: false,
                    Message: "HttpContext not available for client connection test",
                    Tags: new Dictionary<string, string>
                    {
                        { "check_type", "client_connection" },
                        { "error_type", "no_http_context" }
                    }
                );
            }

            string scheme = request.Scheme;
            string host = request.Host.Value;
            string endpoint = $"{scheme}://{host}";

            logger.LogInformation("Testing MCP client connection to endpoint: {Endpoint}", endpoint);

            IMcpClient mcpClient = await McpClientFactory.CreateAsync(
                new SseClientTransport(new()
                {
                    Endpoint = new Uri(endpoint),
                    Name = "HealthCheck"
                }),
                clientOptions: new()
                {
                    Capabilities = new() { }
                },
                loggerFactory: loggerFactory);

            logger.LogInformation("MCP client created successfully, listing tools...");

            IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
            string[] toolNames = mcpTools.Select(t => t.Name).OrderBy(t => t).ToArray();

            logger.LogInformation("Found {ToolCount} tools via MCP client: {Tools}", toolNames.Length, string.Join(", ", toolNames));

            return new CheckResult(
                Name: _clientConnectionCheckName,
                IsHealthy: toolNames.Length != 0,
                Message: toolNames.Length != 0 ? "Client successfully connected and listed tools" : "Client connected but no tools found",
                Tags: new Dictionary<string, string>
                {
                    { "check_type", "client_connection" },
                    { "endpoint", endpoint },
                    { "tools", string.Join(", ", toolNames) },
                    { "tool_count", toolNames.Length.ToString() }
                });
        }
        catch (HttpRequestException httpEx) when (httpEx.Message.Contains("500"))
        {
            return new CheckResult(
                Name: _clientConnectionCheckName,
                IsHealthy: false,
                Message: "MCP SSE endpoint returned 500 error - endpoint may not be properly configured",
                Tags: new Dictionary<string, string>
                {
                    { "check_type", "client_connection" },
                    { "error_type", "http_500" },
                    { "error_message", httpEx.Message }
                }
            );
        }
        catch (HttpRequestException httpEx)
        {
            return new CheckResult(
                Name: _clientConnectionCheckName,
                IsHealthy: false,
                Message: $"HTTP error connecting to MCP server: {httpEx.Message}",
                Tags: new Dictionary<string, string>
                {
                    { "check_type", "client_connection" },
                    { "error_type", "http_error" },
                    { "error_message", httpEx.Message }
                }
            );
        }
        catch (TaskCanceledException tcEx) when (tcEx.InnerException is TimeoutException)
        {
            return new CheckResult(
                Name: _clientConnectionCheckName,
                IsHealthy: false,
                Message: "Timeout connecting to MCP server",
                Tags: new Dictionary<string, string>
                {
                    { "check_type", "client_connection" },
                    { "error_type", "timeout" }
                }
            );
        }
        catch (Exception ex)
        {
            return new CheckResult(
                Name: _clientConnectionCheckName,
                IsHealthy: false,
                Message: $"Client connection check failed: {ex.Message}",
                Tags: new Dictionary<string, string>
                {
                    { "check_type", "client_connection" },
                    { "error_type", "general_error" },
                    { "error_message", ex.Message }
                }
            );
        }
    }

    /// <summary>
    /// Legacy method for backward compatibility - returns service registration check
    /// </summary>
    public static Task<CheckResult> CheckAsync(IServiceProvider serviceProvider)
    {
        return Task.FromResult(CheckServiceRegistration(serviceProvider));
    }
}
