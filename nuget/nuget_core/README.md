# Data API builder Core Library for Azure Databases

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT)

## About

**Microsoft.DataApiBuilder.Core** is the core engine library for [Data API builder](https://learn.microsoft.com/azure/data-api-builder/) (DAB). It provides the runtime components needed to generate and execute secure REST, GraphQL endpoints and MCP tools backed by Azure and other databases.

This package is intended for developers who want to embed or extend the Data API builder engine within their own .NET applications or services.

## Supported Databases

- Azure SQL / SQL Server
- Azure SQL Data Warehouse
- Azure Cosmos DB (NoSQL)
- PostgreSQL
- MySQL

## Key Capabilities

- **REST API engine** — Automatically generates CRUD endpoints (POST, GET, PUT, PATCH, DELETE) with filtering, sorting, and pagination
- **GraphQL engine** — Generates queries and mutations with filtering, sorting, pagination, and relationship navigation
- **MCP tool support** — Exposes DML and custom MCP tools for building SQL MCP Servers
- **Authentication** — OAuth2/JWT and EasyAuth (Azure App Service / Static Web Apps)
- **Authorization** — Role-based access control with item-level security via policy expressions
- **Configuration-driven** — Define entities, permissions, and relationships in a JSON config file; no code required
- **Multi-database** — Connect to multiple database types from a single instance
- **Caching** — Built-in response caching with [FusionCache](https://github.com/ZiggyCreatures/FusionCache)

## Installation

```bash
dotnet add package Microsoft.DataApiBuilder.Core
```

## Usage

This library provides the core services and middleware used by the Data API builder runtime. Register the DAB services in your application's dependency injection container and configure them using a [DAB configuration file](https://learn.microsoft.com/azure/data-api-builder/reference-configuration).

For a complete, ready-to-run experience, consider using the [`Microsoft.DataApiBuilder`](https://www.nuget.org/packages/Microsoft.DataApiBuilder) NuGet package or the [DAB CLI](https://learn.microsoft.com/azure/data-api-builder/how-to-install-cli).

## Resources

- [Official Documentation](https://learn.microsoft.com/azure/data-api-builder/)
- [GitHub Repository](https://github.com/Azure/data-api-builder)
- [Samples](https://aka.ms/dab/samples)
- [Known Issues](https://learn.microsoft.com/azure/data-api-builder/known-issues)
