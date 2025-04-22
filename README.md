# Data API builder for Azure Databases

[![NuGet Package](https://img.shields.io/nuget/v/microsoft.dataapibuilder.svg?color=success)](https://www.nuget.org/packages/Microsoft.DataApiBuilder)
[![Nuget Downloads](https://img.shields.io/nuget/dt/Microsoft.DataApiBuilder)](https://www.nuget.org/packages/Microsoft.DataApiBuilder)
[![Documentation](https://img.shields.io/badge/docs-website-%23fc0)](https://learn.microsoft.com/azure/data-api-builder/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT)

[What's new?](https://learn.microsoft.com/azure/data-api-builder/whats-new)

## Community

Join the Data API builder community! This sign up will help us maintain a list of interested developers to be part of our roadmap and to help us better understand the different ways DAB is being used. Sign up [here](https://forms.office.com/pages/responsepage.aspx?id=v4j5cvGGr0GRqy180BHbR1S1JdzGAxhDrefV-tBYtwZUNE1RWVo0SUVMTkRESUZLMVVOS0wwUFNVRy4u).

![](docs/media/dab-logo.png)

## About Data API builder

Data API builder (DAB) is an open-source, no-code tool that creates secure, full-featured REST and GraphQL endpoints for your database. It’s a CRUD data API engine that runs in a container—on Azure, any other cloud, or on-premises. DAB is built for developers with integrated tooling, telemetry, and other productivity features.

```mermaid
erDiagram
    DATA_API_BUILDER ||--|{ DATA_API : "Provides"
    DATA_API_BUILDER {
        container true "Microsoft Container Repository"
        open-source true "MIT license / any cloud or on-prem."
        objects true "Supports: Table / View / Stored Procedure"
        developer true "Swagger / Nitro (fka Banana Cake Pop)"
        otel true "Open Telemetry / Structured Logs / Health Endpoints"
        security true "EntraId / EasyAuth / OAuth / JWT / Anonymous"
        cache true "Level1 (in-memory) / Level2 (redis)"
        policy true "Item policy / Database policy / Claims policy"
        hot_reload true "Dynamically controllable log levels"
    }
    DATA_API ||--o{ DATASOURCE : "Queries"
    DATA_API {
        REST true "$select / $filter / $orderby"
        GraphQL true "relationships / multiple mutations"
    }
    DATASOURCE {
        MS_SQL Supported
        PostgreSQL Supported
        Cosmos_DB Supported
        MySQL Supported
        SQL_DW Supported
    }
    CLIENT ||--o{ DATA_API : "Consumes"
    CLIENT {
        Transport HTTP "HTTP / HTTPS"
        Syntax JSON "Standard payloads"
        Mobile Supported "No requirement"
        Web Supported "No requirement"
        Desktop Supported "No requirement"
        Language Any "No requirement"
        Framework None "Not required"
        Library None "Not required"
        ORM None "Not required"
        Driver None "Not required"
    }
```

## Getting Started

Use the [Getting Started](https://learn.microsoft.com/azure/data-api-builder/get-started/get-started-with-data-api-builder) tutorial to quickly explore the core tools and concepts. It gives you hands-on experience with how DAB makes you more efficient by removing boilerplate code.

**1. Install the DAB CLI**

The [DAB CLI](https://aka.ms/dab/docs) is a cross-platform .NET tool. Install the [.NET SDK](https://get.dot.net) before running:

```
dotnet tool install microsoft.dataapibuilder -g
```

**2. Create your initial configuration file**

DAB requires a JSON configuration file. Edit manually or with the CLI. Use `dab --help` for syntax options.

```
dab init
  --database-type mssql
  --connection-string "@env('my-connection-string')"
  --host-mode development
```

**3. Add your first table**

DAB supports tables, views, and stored procedures. It works with SQL Server, Azure Cosmos DB, PostgreSQL, MySQL, and SQL Data Warehouse. Security is engine-level, but permissions are per entity.

```
dab add Actor
  --source "dbo.Actor"
  --permissions "anonymous:*"
```

**4. Run Data API builder**

In `production`, DAB runs in a container. In `development`, it’s self-hosted locally with hot reload, Swagger, and Nitro (fka Banana Cake Pop) support.

```
dab start
```

> **Note**: Before you run `dab start`, make sure your connection string is stored in an environment variable called `my-connection-string`. This is required for `@env('my-connection-string')` in your config file to work. The easiest way is to create a `.env` file with `name=value` pairs—DAB will load these automatically at runtime.

**5. Access your data source**

By default, DAB enables both REST and GraphQL. REST supports `$select`, `$filter`, and `$orderBy`. GraphQL uses config-defined relationships.

```
GET http://localhost:5000/api/Actor
```

### Walk-through video

<a href="https://www.youtube.com/watch?v=xAlaoDQolLw" target="_blank">
  <img src="https://img.youtube.com/vi/xAlaoDQolLw/0.jpg" alt="Play Video" width="280" />
</a>

Demo source code: [startrek](https://aka.ms/dab/startrek)

## Overview

| Category       | Features |
|----------------|----------|
| **Database Objects** | • NoSQL collections<br>• RDBMS tables, views, stored procedures |
| **Data Sources** | • SQL Server & Azure SQL<br>• Azure Cosmos DB<br>• PostgreSQL<br>• MySQL |
| **REST** | • `$select` for projection<br>• `$filter` for filtering<br>• `$orderBy` for sorting |
| **GraphQL** | • Relationship navigation<br>• Data aggregation<br>• Multiple mutations |
| **Telemetry** | • Structured logs<br>• OpenTelemetry<br>• Application Insights<br>• Health endpoints |
| **Advanced** | • Pagination<br>• Level 1 (in-memory) cache<br>• Level 2 (Redis) cache |
| **Authentication** | • OAuth2/JWT<br>• EasyAuth<br>• Entra ID |
| **Authorization** | • Role-based support<br>• Entity permissions<br>• Database policies |
| **Developer** | • Cross-platform CLI<br>• Swagger (REST)<br>• Banana Cake Pop (GraphQL)<br>• Open Source<br>• Hot Reload |

## How does it work?

This diagram shows how DAB works. DAB dynamically creates endpoints from your config file. It translates HTTP requests to SQL, returns JSON, and auto-pages results.

```mermaid
sequenceDiagram
    actor Client

    box Data API builder (DAB)
        participant Endpoint
        participant QueryBuilder
    end

    participant Configuration as Configuration File

    box Data Source
        participant DB
    end

    Endpoint->>Endpoint: Start
    activate Endpoint
        Endpoint->>Configuration: Request
        Configuration-->>Endpoint: Configuration
        Endpoint->>DB: Request
        DB-->>Endpoint: Metadata
            Note over Endpoint, DB: Some configuration is validated against the metadata
        Endpoint-->>Endpoint: Configure
    deactivate Endpoint
    Client-->>Endpoint: HTTP Request
    activate Endpoint
        critical
        Endpoint-->>Endpoint: Authenticate
        Endpoint-->>Endpoint: Authorize
        end
        Endpoint->>QueryBuilder: Request
        QueryBuilder-->>Endpoint: SQL
        alt Cache
            Endpoint-->>Endpoint: Use Cache
        else Query
            Endpoint-->>DB: Request
            Note over Endpoint, DB: Query is automatically throttled and results paginated
            DB->>Endpoint: Results
            Note over Endpoint, DB: Results are automatically cached for use in next request
        end
        Endpoint->>Client: HTTP 200
    deactivate Endpoint
```

Because DAB is stateless, it can scale up or out using any container size. It builds a feature-rich API like you would from scratch—but now you don’t have to.

## Additional Resources

- [Online Documentation](https://aka.ms/dab/docs)  
- [Official Samples](https://aka.ms/dab/samples)  
- [Known Issues](https://learn.microsoft.com/azure/data-api-builder/known-issues)  
- [Feature Roadmap](https://github.com/Azure/data-api-builder/discussions/1377)

#### References

- [Microsoft REST API Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/Guidelines.md)  
- [Microsoft Azure REST API Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md)  
- [GraphQL Specification](https://graphql.org/)

### How to Contribute

To contribute, see these documents:

- [Code of Conduct](./CODE_OF_CONDUCT.md)  
- [Security](./SECURITY.md)  
- [Contributing](./CONTRIBUTING.md)

### License

**Data API builder for Azure Databases** is licensed under the MIT License. See [LICENSE](./LICENSE.txt) for details.

### Third-Party Component Notice

Nitro (fka Banana Cake Pop by ChilliCream, Inc.) may optionally store work in its cloud service via your ChilliCream account. Microsoft is not affiliated with or endorsing this service. Use at your discretion.

### Trademarks

This project may use trademarks or logos. Use of Microsoft trademarks must follow Microsoft’s [Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks). Use of third-party marks is subject to their policies.
