# Data API builder for Azure Databases

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT)

Latest version of Data API builder is  **0.2.52** (aka **Sept2022** release)

## About

**Data API builder for Azure Databases provides modern REST and GraphQL endpoints to your Azure Databases.**

With Data API builder, database objects can be exposed via REST or GraphQL endpoints so that your data can be accessed using modern techniques on any platform, any language, and any device. With an integrated and flexible policy engine, granular security is assured; integrated with Azure SQL, SQL Server, PostgreSQL, MySQL, MariaDB and Cosmos DB, gives developers an efficiency boost like never seen before.

![Data API Builder Architecture Overview Diagram](./docs/media/data-api-builder-overview.png)

## 

Data API builder is completely transparent to your database. It doesn't require any modification to your schema or data. It doesn't create any database object. It doesn't require any special naming convention to be followed or implemented. Data API builder just does the heavy lifting for you, and it will 

## Features

- Allow collections, tables and views to be accessed via REST and GraphQL
- Support authentication via JWT and EasyAuth
- Role-based authorization using received claims
- Item-level security via policy expressions
- REST
  - CRUD operations via POST, GET, PUT, PATCH, DELETE
  - filtering, sorting and pagination
- GraphQL
  - queries and mutations
  - filtering, sorting and pagination
  - relationship navigation
- Easy development via dedicated CLI

## Current limitations

- JWT only supports Azure AD
- Tables must have a primary key
- MySQL, MariaDB and PostgreSQL are not yet fully supported

## Known Issues

List of known issues and possible workarounds, where applicable and possible, is available here: [Known Issues](./docs/known-issues.md).

## Getting Started

To get started quickly with Data API builder for Azure Databases, you can use the [Getting Started](./docs/getting-started/getting-started.md) tutorial, that will help to get familiar with some basic tools and concepts while giving you a good experience on how much Data API builder for Azure Databases can make you more efficient, but removing the need to write a lot of plumbing code.

## Documentation

Documentation is available in the [`docs`](./docs) folder.

## How to Contribute

Contributions to this project are more than welcome. Make sure you check out the following documents, to successfully contribute to the project:

- [Code Of Conduct](./CODE_OF_CONDUCT.md)
- [Security](./SECURITY.md)
- [Contributing](./CONTRIBUTING.md)

