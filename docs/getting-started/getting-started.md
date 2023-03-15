# Getting started with Data API builder for Azure Databases

Welcome! In this guide we will help you get started with Data API builder (DAB) for Azure Databases. First you are going to get DAB running locally on your machine. Then you will use DAB to create an API for your application. You will have the option to choose between Azure SQL Database, Azure Cosmos DB for NoSQL, Azure Database for MySQL or PostgreSQL as the database backend.

## Use case scenario

In this tutorial we'll be creating the backend API for a small solution that allows end-users to keep track of books in their bookshelf. Therefore, the business entities we'll be dealing with are:

- Books
- Authors

Both the business entities need a modern endpoint, REST and/or GraphQL, to allow third party developers to build mobile and desktop applications to manage the library catalog. Data API builder is perfect for enabling that modern endpoint support.

## Prerequisites

### .NET 6 SDK

Make sure you have .NET 6.0 SDK installed on your machine: https://dotnet.microsoft.com/en-us/download/dotnet/6.0.

You can list the SDKs installed on your machine by using the following command:

```shell
dotnet --list-sdks
```

## Installing DAB CLI

Data API builder provides a CLI tool to simplify configuration and execution of the engine. You can install the DAB CLI using [.NET tools](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools):

```shell
dotnet tool install --global Microsoft.DataApiBuilder 
```

or, if you have already installed a previous version, you can update DAB CLI to the latest version via the following:

```shell
dotnet tool update --global Microsoft.DataApiBuilder 
```

> **ATTENTION**: if you are running on Linux or MacOS, you may need to add .NET global tools to your PATH to call `dab` directly. Once installed run:
> `export PATH=$PATH:~/.dotnet/tools`

## Verifying the installation

Installing the package will make the `dab` command available on your development machine. To validate your installation, you can run the following command:

```bash
dab --version
```

which should output something like:

```bash
dab 0.5.34
```

Where `0.5.34` is the version of DAB CLI that you have installed on your machine.

## Azure Database

As the Data API builder for Azure Databases generates REST and GraphQL endpoints for database objects, you need to have a database ready for the tutorial. You can choose either a relational or non-relational database. 

It's time for you to choose which database you want to use, so you can continue the getting started guide from there:

- [Getting Started with Data API builder for Azure SQL (or SQL Server)](./getting-started-azure-sql.md)
- [Getting Started with Data API builder for Azure Cosmos DB](./getting-started-azure-cosmos-db.md)
- [Getting Started with Data API builder for Azure Database PostgreSQL](./getting-started-azure-postgresql.md)
- [Getting Started with Data API builder for Azure MySQL Database](./getting-started-azure-mysql-db.md)

## Further reading

### Running Data API builder using Docker

You can use Docker to run the Data API builder on your machine. Instructions are available here: [Running Data API builder for Azure Databases using a container](../running-using-a-container.md)

### Using Data API builder CLI to build the configuration file

Data API builder comes with a full CLI to help you run common tasks. Instructions on how to run with CLI are here: [Running using dab cli](../running-using-dab-cli.md)
The list of supported CLI commands is available here: [Data API builder (`dab`) CLI](../dab-cli.md).

### Deploy on Azure

Data API Builder can run in Azure so that you can easily build scalable applications. Detailed explanation - along with a sample script to help you getting started - is available here: [Running in Azure](./../running-in-azure.md)
