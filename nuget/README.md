# Data API builder for Azure Databases

[![NuGet Package](https://img.shields.io/nuget/v/microsoft.dataapibuilder.svg?color=success)](https://www.nuget.org/packages/Microsoft.DataApiBuilder)
[![Nuget Downloads](https://img.shields.io/nuget/dt/Microsoft.DataApiBuilder)](https://www.nuget.org/packages/Microsoft.DataApiBuilder)
[![Documentation](https://img.shields.io/badge/docs-website-%23fc0)](https://learn.microsoft.com/azure/data-api-builder/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT)

[What's new?](https://learn.microsoft.com/azure/data-api-builder/whats-new)

## About

**Data API builder (DAB) is an open-source, no-code tool that creates secure, full-featured REST, GraphQL, and MCP endpoints for your database.**

With data API builder, database objects can be exposed via REST, GraphQL, or MCP endpoints so that your data can be accessed using modern techniques on any platform, any language, and any device. With an integrated and flexible policy engine, native support for common behavior like pagination, filtering, projection and sorting, the creation of CRUD backend services and MCP tools can be done in minutes instead of hours or days, giving developers an efficiency boost like never seen before.

Data API builder is Open Source and works on any platform. It can be executed on-premises, in a container or as a Managed Service in Azure, via the [Database Connection](https://learn.microsoft.com/azure/static-web-apps/database-overview) feature available in Azure Static Web Apps.

### Which databases does Data API builder support?

|               | Azure SQL | SQL Server | SQLDW | Cosmos DB | PostgreSQL | MySQL |
| :-----------: | :-------: | :--------: | :---: | :-------: | :--------: | :---: |
| **Supported** |    Yes    |     Yes    |  Yes  |    Yes    |     Yes    |  Yes  |

### Which environments does Data API builder support?

|               | On-Prem | Azure |  AWS |  GCP | Other |
| :-----------: | :-----: | :---: | :--: | :--: | :---: |
| **Supported** |   Yes   |  Yes  |  Yes |  Yes |  Yes  |

### Which endpoints does Data API builder support?

|               | REST | GraphQL | MCP |
| :-----------: | :--: | :-----: | :-: |
| **Supported** |  Yes |   Yes   | Yes |

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
  - Exposes database tables, views, and stored procedures as MCP tools
  - Compatible with any MCP-enabled AI client (e.g., GitHub Copilot, Azure AI Foundry)
  - DML tools for create, read, update, and delete operations
  - Custom tools via stored procedures
  - Configurable endpoint path (default: `/mcp`)
- Easy development via dedicated CLI
- Full integration with Static Web Apps via Database Connection feature when running in Azure
- Open Source

## Getting started

Use the [Getting Started](https://learn.microsoft.com/azure/data-api-builder/get-started/get-started-with-data-api-builder) tutorial to quickly explore the core tools and concepts.

### 1. Install the `dotnet` [command line](https://get.dot.net)

The Data API builder (DAB) command line requires the .NET runtime version 8 or later.

```sh
dotnet --version
```

### 2. Install the `dab` command line

```sh
dotnet tool install microsoft.dataapibuilder -g
```

```sh
dab --version
```

### 3. Create your initial configuration file

```sh
dab init \
  --database-type mssql \
  --connection-string "@env('my-connection-string')" \
  --host-mode development
```

> **Note:** Including `--host-mode development` enables Swagger for REST, Nitro for GraphQL, and the MCP endpoint for Model Context Protocol clients.

### 4. Add a table to the configuration

```sh
dab add Todo \
  --source "dbo.Todo" \
  --permissions "anonymous:*"
```

### 5. Run Data API builder

```sh
dab start
```

### 6. Access your data

By default, DAB enables REST, GraphQL, and MCP.

```
GET http://localhost:5000/api/Todo
```

#### Other endpoints to explore

- DAB's Health endpoint: `http://localhost:5000/health`
- DAB's Swagger UI: `http://localhost:5000/api/openapi`
- DAB's Nitro UI: `http://localhost:5000/graphql`
- DAB's MCP endpoint: `http://localhost:5000/mcp`

## Additional resources

- [Online Documentation](https://aka.ms/dab/docs)
- [Official Samples](https://aka.ms/dab/samples)
- [Known Issues](https://learn.microsoft.com/azure/data-api-builder/known-issues)
- [Feature Roadmap](https://github.com/Azure/data-api-builder/discussions/1377)

### How to contribute

To contribute, see these documents:

- [Code of Conduct](https://github.com/Azure/data-api-builder/blob/main/CODE_OF_CONDUCT.md)
- [Security](https://github.com/Azure/data-api-builder/blob/main/SECURITY.md)
- [Contributing](https://github.com/Azure/data-api-builder/blob/main/CONTRIBUTING.md)
- [MIT License](https://github.com/Azure/data-api-builder/blob/main/LICENSE.txt)

### Third-party component notice

Nitro (formerly Banana Cake Pop by ChilliCream, Inc.) may optionally store work in its cloud service via your ChilliCream account. Microsoft is not affiliated with or endorsing this service. Use at your discretion.

### Trademarks

This project may use trademarks or logos. Use of Microsoft trademarks must follow Microsoft's [Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks). Use of third-party marks is subject to their policies.
