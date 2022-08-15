# Data API builder for Azure Databases

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT)

Latest version of Data API builder is  **0.1.5** also known as **M1.5**

## About

**Data API builder for Azure Databases provide modern REST and GraphQL endpoints to your Azure Databases.**

With Data API builder, database objects can be exposed via REST or GraphQL endpoints so that your data can be accessed using modern techniques by any platform, any language, any device. With an integrated and flexible policy engine, granular security is assured; integrated with Azure SQL DB, SQL Server, PostgreSQL, MySQL, MariaDB and Cosmos DB, gives developer an efficiency boost like it was never seen before.

## Features

- Allow collections, tables and views to be accessed via REST and GraphQL
- Support authentication via JWT and EasyAuth
- Role-based authorization using received claims
- Item-level security via policy expressions
- REST 
  - CRUD operations via POST, GET, PUT, DELETE
  - filtering, sorting and pagination
- GraphQL 
  - queries and mutations
  - filtering, sorting and pagination
  - relationship navigation

## Limitations 

- JWT only supports AAD
- REST does not support partial updates (PATCH)

## Known Issues

List of known issues and possible workarounds,where applicable and possible, is availabe here: [Known Issues](./docs/known-issues.md).

## Getting Started

To get started quickly with Data API builder for Azure Databases, you can use the [Getting Started](./docs/getting-started/getting-started.md) tutorial, that will help to get familiar with some basic tools and concepts while giving you a good experience on how much Data API builder for Azure Databases can make you more efficient, but removing the need to write a lot of plumbing code.

## Documentation

Documentation is available in the [`docs`](./docs) folder.

## How to Contribute

Contributions to this project are more than welcome. Make sure you check out the following documents, to successfully contribute to the project:

- [Code Of Conduct](./CODE_OF_CONDUCT.md)
- [Security](./SECURITY.md)
- [Contributing](./CONTRIBUTING.md)

