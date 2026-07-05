# Data API builder for Azure Databases

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![Documentation](https://img.shields.io/badge/docs-website-%23fc0)](https://learn.microsoft.com/azure/data-api-builder/)

[What's new?](https://learn.microsoft.com/azure/data-api-builder/whats-new)

## About

**Data API builder for Azure Databases provides modern REST and GraphQL endpoints and MCP tools to your Azure Databases.**

With data API builder, database objects can be exposed via REST or GraphQL endpoints as well as MCP tools so that your data can be accessed using modern techniques on any platform, any language, and any device. With an integrated and flexible policy engine, native support for common behavior like pagination, filtering, projection and sorting, the creation of CRUD backend services and MCP tools can be done in minutes instead of hours or days, giving developers an efficiency boost like never seen before.

Data API builder is Open Source and works on any platform. It can be executed on-premises, in a container or as a Managed Service in Azure, via the [Database Connection](https://learn.microsoft.com/azure/static-web-apps/database-overview) feature available in Azure Static Web Apps.

## Features

- Allow collections, tables, views and stored procedures to be accessed via REST, GraphQL, and MCP
- Support authentication via OAuth2/JWT
- Support for EasyAuth when running in Azure
- Role-based authorization using received claims
- Item-level security via policy expressions
- REST
  - CRUD operations via POST, GET, PUT, PATCH, DELETE
  - Filtering, sorting and pagination
- GraphQL
  - Queries and mutations
  - Filtering, sorting and pagination
  - Relationship navigation
- MCP (Model Context Protocol)
  - Exposes database tables, views, and stored procedures through MCP tools
  - Compatible with any MCP-enabled AI client (e.g., GitHub Copilot, Azure AI Foundry)
  - DML tools for create, read, update, and delete operations
  - Custom tools via stored procedures
- Easy development via dedicated CLI
- Full integration with Static Web Apps via Database Connection feature when running in Azure
- Open Source

## Getting started

Use the [Getting Started](https://learn.microsoft.com/azure/data-api-builder/get-started/get-started-with-data-api-builder) tutorial to quickly explore the core tools and concepts.

#### Other endpoints to explore

- DAB's Health endpoint: `http://localhost:5000/health`
- DAB's Swagger UI: `http://localhost:5000/api/openapi`

## Additional resources

- [Online Documentation](https://learn.microsoft.com/en-us/azure/data-api-builder/overview)
- [Official Samples](https://github.com/Azure-samples/data-api-builder)
- [Known Issues](https://learn.microsoft.com/azure/data-api-builder/known-issues)