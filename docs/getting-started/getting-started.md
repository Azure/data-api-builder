# Getting started with Data API builder for Azure Databases

Welcome! In this guide we will help you get started with Data API builder (DAB) for Azure Databases. First you are going to get DAB running locally on your machine. Then you will use DAB to create an API for your application. You will have the option to choose between Azure SQL Database or Azure Cosmos DB as the database backend.

## Use case scenario

In this tutorial we'll be creating the backend API for a small solution that allows end-users to keep track of books in their bookshelf. Therefore, the business entities we'll be dealing with are:

- Books
- Authors

Both the business entities need a modern endpoint, REST and/or GraphQL, to allow third party developers to build mobile and desktop applications to manage the library catalog. Data API builder is perfect for enabling that modern endpoint support.

## Prerequisites

### .NET 6 SDK

Make sure you have .NET 6.0 SDK installed on your machine: https://dotnet.microsoft.com/en-us/download/dotnet/6.0.

You can list the SDKs installed on your machine by using the following command:

```bash
dotnet --list-sdks
```

## Installing DAB CLI

Data API Builder provides a CLI tool to simplify configuration and execution of the engine. You can install the DAB CLI using [.NET tools](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools):

- Download the latest version of the package: [dab.<version_number>.nupkg](https://github.com/Azure/data-api-builder/releases/)
- Navigate to the folder where the package file is downloaded.

then, to install this tool globally, use:

```bash
dotnet tool install Microsoft.DataApiBuilder --global --version <version_number>
```

or, if you have already installed a previous version, you can update DAB CLI to the latest version via the following:

```bash
dotnet tool update --global Microsoft.DataApiBuilder --version <version_number>
```

> **ATTENTION**: if you are running on Linux or MacOS, you may need to add .NET global tools to your PATH to call `dab` directly. Once installed run:
> `export PATH=$PATH:~/.dotnet/tools`

## Verifying the installation

Installing the package will make the `dab` command available on your development machine. To validate your installation, you can run the following command:

```bash
dab --version
```

which should output

```bash
dab 0.1.5
```

Where `0.1.5` should match your version of DAB CLI.

>For detailed instructions on how to Install DAB CLI look here: [Running Data API Builder for Azure Databases using CLI](../running-using-dab-cli.md)

## Azure Database

As the Data API builder for Azure Databases generates REST and GraphQL endpoints for database objects, you need to have a database ready for the tutorial. You can choose either a relational or non-relational database. This getting started guide documents the process to set up Data API builder for Azure SQL or Azure Cosmos DB.

It's time for you to choose which database you want to use, so you can continue the getting started guide from there:

- [Getting Started with Data API builder for Azure SQL](./getting-started-azure-sql.md)
- [Getting Started with Data API builder for with Azure Cosmos DB](./getting-started-azure-cosmos-db.md)

## Further reading

### Running Data API Builder using Docker

You can use Docker to run the Data API Builder on your machine. Instructions are available here: [Running Data API Builder for Azure Databases using a container](../running-using-a-container.md)

### Using Data API Builder CLI to build the configuration file

Data API Builder comes with a full CLI to help you run common tasks [Getting started with Data API Builder (`dab`) CLI](../getting-started/getting-started-dab-cli.md).

### Deploy on Azure

Data API Builder can run in Azure so that you can easily build scalable applications. Detailed explanation - along with a sample script to help you getting started - is available here: [Running in Azure](./../running-in-azure.md)
