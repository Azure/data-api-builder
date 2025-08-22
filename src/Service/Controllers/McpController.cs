// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Controllers;

/// <summary>
/// Controller for Model Context Protocol (MCP) endpoints.
/// Provides AI agentic applications with standardized access to DAB operations.
/// </summary>
[ApiController]
[Route("/mcp")]
public class McpController : ControllerBase
{
    private readonly ILogger<McpController> _logger;
    private readonly RuntimeConfigProvider _runtimeConfigProvider;

    public McpController(
        ILogger<McpController> logger,
        RuntimeConfigProvider runtimeConfigProvider)
    {
        _logger = logger;
        _runtimeConfigProvider = runtimeConfigProvider;
    }

    /// <summary>
    /// MCP Tools endpoint - Lists available tools (entities and operations).
    /// </summary>
    /// <returns>MCP tools list response</returns>
    [HttpPost("tools")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult ListTools()
    {
        RuntimeConfig config = _runtimeConfigProvider.GetConfig();

        if (!config.IsMcpEnabled)
        {
            return NotFound("MCP endpoint is disabled");
        }

        try
        {
            var tools = new List<object>();

            // Add entity-based tools
            foreach (var entity in config.Entities)
            {
                string entityName = entity.Key;
                Entity entityConfig = entity.Value;

                // Add CRUD operations as tools
                if (entityConfig.Permissions != null)
                {
                    foreach (var permission in entityConfig.Permissions)
                    {
                        string roleName = permission.Role;
                        EntityAction[] roleActions = permission.Actions;

                        // Add read tool if permitted
                        if (roleActions?.Any(a => a.Action == EntityActionOperation.Read) == true)
                        {
                            tools.Add(new
                            {
                                name = $"read_{entityName}",
                                description = $"Read data from {entityName} entity",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        filter = new { type = "string", description = "Filter criteria for the query" },
                                        select = new { type = "string", description = "Fields to select" },
                                        first = new { type = "integer", description = "Number of records to return" }
                                    }
                                }
                            });
                        }

                        // Add create tool if permitted
                        if (roleActions?.Any(a => a.Action == EntityActionOperation.Create) == true)
                        {
                            tools.Add(new
                            {
                                name = $"create_{entityName}",
                                description = $"Create new record in {entityName} entity",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        data = new { type = "object", description = "Data for the new record" }
                                    },
                                    required = new[] { "data" }
                                }
                            });
                        }

                        // Add update tool if permitted
                        if (roleActions?.Any(a => a.Action == EntityActionOperation.Update) == true)
                        {
                            tools.Add(new
                            {
                                name = $"update_{entityName}",
                                description = $"Update record in {entityName} entity",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        id = new { type = "string", description = "Primary key of the record to update" },
                                        data = new { type = "object", description = "Updated data" }
                                    },
                                    required = new[] { "id", "data" }
                                }
                            });
                        }

                        // Add delete tool if permitted
                        if (roleActions?.Any(a => a.Action == EntityActionOperation.Delete) == true)
                        {
                            tools.Add(new
                            {
                                name = $"delete_{entityName}",
                                description = $"Delete record from {entityName} entity",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        id = new { type = "string", description = "Primary key of the record to delete" }
                                    },
                                    required = new[] { "id" }
                                }
                            });
                        }
                    }
                }
            }

            var response = new
            {
                tools = tools.DistinctBy(t => ((dynamic)t).name).ToList()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing MCP tools");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// MCP Call Tool endpoint - Executes a specific tool with provided arguments.
    /// </summary>
    /// <param name="request">Tool execution request</param>
    /// <returns>Tool execution result</returns>
    [HttpPost("call")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult CallTool([FromBody] McpCallRequest request)
    {
        RuntimeConfig config = _runtimeConfigProvider.GetConfig();

        if (!config.IsMcpEnabled)
        {
            return NotFound("MCP endpoint is disabled");
        }

        if (request == null || string.IsNullOrEmpty(request.Name))
        {
            return BadRequest("Tool name is required");
        }

        try
        {
            // Parse tool name to determine entity and operation
            var toolParts = request.Name.Split('_', 2);
            if (toolParts.Length != 2)
            {
                return BadRequest("Invalid tool name format. Expected: {operation}_{entity}");
            }

            string operation = toolParts[0];
            string entityName = toolParts[1];

            if (!config.Entities.ContainsKey(entityName))
            {
                return BadRequest($"Entity '{entityName}' not found");
            }

            // For now, return a placeholder response
            // In a full implementation, this would delegate to the appropriate service
            var response = new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = $"Tool '{request.Name}' would execute {operation} operation on {entityName} entity with arguments: {JsonSerializer.Serialize(request.Arguments)}"
                    }
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling MCP tool: {ToolName}", request.Name);
            return StatusCode(500, "Internal server error");
        }
    }
}

/// <summary>
/// Request model for MCP tool calls.
/// </summary>
public class McpCallRequest
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object>? Arguments { get; set; }
}