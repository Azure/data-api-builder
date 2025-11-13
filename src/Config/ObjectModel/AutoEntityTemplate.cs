// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Defines the template configuration for auto-entities.
/// </summary>
/// <param name="Mcp">MCP endpoint configuration</param>
/// <param name="Rest">REST endpoint configuration</param>
/// <param name="GraphQL">GraphQL endpoint configuration</param>
/// <param name="Health">Health check configuration</param>
/// <param name="Cache">Cache configuration</param>
public record AutoEntityTemplate(
    [property: JsonPropertyName("mcp")] AutoEntityMcpTemplate? Mcp = null,
    [property: JsonPropertyName("rest")] AutoEntityRestTemplate? Rest = null,
    [property: JsonPropertyName("graphql")] AutoEntityGraphQLTemplate? GraphQL = null,
    [property: JsonPropertyName("health")] AutoEntityHealthTemplate? Health = null,
    [property: JsonPropertyName("cache")] EntityCacheOptions? Cache = null
);

/// <summary>
/// MCP template configuration for auto-entities.
/// </summary>
/// <param name="DmlTool">Enable/disable DML tool (default: true)</param>
public record AutoEntityMcpTemplate(
    [property: JsonPropertyName("dml-tool")] bool DmlTool = true
);

/// <summary>
/// REST template configuration for auto-entities.
/// </summary>
/// <param name="Enabled">Enable/disable REST endpoint (default: true)</param>
public record AutoEntityRestTemplate(
    [property: JsonPropertyName("enabled")] bool Enabled = true
);

/// <summary>
/// GraphQL template configuration for auto-entities.
/// </summary>
/// <param name="Enabled">Enable/disable GraphQL endpoint (default: true)</param>
public record AutoEntityGraphQLTemplate(
    [property: JsonPropertyName("enabled")] bool Enabled = true
);

/// <summary>
/// Health check template configuration for auto-entities.
/// </summary>
/// <param name="Enabled">Enable/disable health check endpoint (default: true)</param>
public record AutoEntityHealthTemplate(
    [property: JsonPropertyName("enabled")] bool Enabled = true
);
