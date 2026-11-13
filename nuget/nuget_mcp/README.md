# Data API builder MCP Library

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT)

## About

**Microsoft.DataApiBuilder.Mcp** provides Model Context Protocol (MCP) integration for [Data API builder](https://learn.microsoft.com/azure/data-api-builder/) (DAB).

This package is intended for teams that want to host DAB MCP tools in their own .NET applications.

## Key capabilities

- Registers DAB MCP services in dependency injection
- Maps DAB MCP endpoints for HTTP hosting
- Uses Data API builder Core capabilities for entity and tool execution

## Installation

```bash
dotnet add package Microsoft.DataApiBuilder.Mcp
```

## Usage

This package is designed for ASP.NET Core applications that expose MCP endpoints.

Use it to register DAB MCP services in dependency injection and map DAB MCP endpoints in your app's hosting pipeline.

Current distribution scope is internal Azure Artifacts feeds.

## Resources

- [Official Documentation](https://learn.microsoft.com/azure/data-api-builder/)
- [GitHub Repository](https://github.com/Azure/data-api-builder)
- [Samples](https://aka.ms/dab/samples)
