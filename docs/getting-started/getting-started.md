# Getting started with Data API builder for Azure Databases

Welcome to this guide that will help you get started with Data API builder (DAB) for Azure Databases. First your are going to get it running locally on your machine. Then we will show you how to deploy Data API builder for Azure Databases in an Azure Container Instance. At the end of the tutorial you'll understand what's needed to run Data API builder for Azure Databases both on-prem and on Azure.

## Prerequisites

### .NET 6 SDK

Make sure you have .NET 6.0 SDK installed on your machine: https://dotnet.microsoft.com/en-us/download/dotnet/6.0.

You can list the SDKs installed in your machine by using the following command:

```bash
dotnet --list-sdks
```

### Azure Database

As the Data API builder for Azure Databases generates REST and GraphQL endpoints for database objects, you need to have a database ready fr the tutorial. You can choose either a relational or non-relational database. This getting started guide documents the process to have Data API builder set up for:

- Azure SQL 
- Azure Cosmos DB

## Installing DAB CLI

Data API Builder provides a CLI tool to simplify configuration and execution of the engine. Detailed description on how DAB CLI can be installed on your machine can be found here: [Running Data API Builder for Azure Databases using CLI](../running-using-dab-cli.md)

## Getting to know the configuration file

The Data API builder for Azure Databases engine needs a [configuration file](../configuration-file.md). There you'll define which database DAB connects to, and which entities are to be exposed, together with their properties.

Let's start with an overview of a basic configuration file, and then we'll move to initializing one using the DAB CLI.

### Overview of configuration file

A basic configuration file looks like the following:

```json
{
    "$schema": "../../schemas/dab.draft-01.schema.json",
    "data-source": {
        "database-type": "",
        "connection-string": ""
    },
    "runtime": {
        "host": {
            "mode": "development"
        }
    },
    "entities": {
    }
}
```

The `$schema` property points to the JSON schema that can be used to validate the configuration file. IDEs like like Visual Studio Code will use this proeperty to provide intellisense and autocomplete features.

In the `data-source` section you have to specify the database type and the connection string to connect to the database containing the objects you want to expose.

`database-type` can be any of the following:
- `mssql`: for Azure SQL DB, Azure SQL MI or SQL Server
- `cosmos`: for Azure Cosmos DB (SQL API)
- `postgresql`: for PostgreSQL
- `mysql`: for MySQL or MariaDB

Set the value of `database-type` to the database you want to use. For this tutorial we'll be using `mssql` or `cosmos` to showcase both DAB support for relational and non-relational databases.

Once you have chosen the database you want to connect to, you need to provide the connection string in the `connection-string` property. This is a standard ADO.NET connection string. You can get it from the Azure Portal, for instance. 

The `runtime` section is telling Data API builder to run in `development` mode. This means database errors will be surfaced and returned with full detail. This is great for development, but can be a security risk when running in production: that's why switching to `production` mode will disable this debug feature.

The last property is `entities`. You'll leave it empty for now. This property will contain all the objects you want to be exposed as REST or GraphQL endpoints.

### Initialize the configuration file

Initializing a configuration file can be done with the Data API builder CLI as well: 

```bash
dab init --database-type "<database-type>" --connection-string "<connection-string>"
```

where for `database-type` and `connection-string` you can use any of the values described in the [Overview of configuration file](#overview-of-configuration-file) section above. If you don't know the connection string, no worries, we'll take care of that later, in one of the database-specific Getting Started documents we ave created for you.

## The sample scenario

In this tutorial we'll be creating the backend API for a small solution that allow end-users to keep track books in their library. Therefore the business entities we'll be dealing with are

- Books
- Authors

Both the business entities need a modern endpoint, REST and/or GraphQL, to allow third party developers to build mobile and desktop applications to manage the library catalog. Data API builder is perfect for enabling that modern endpoint support.

## Configure the Entities

Depending if you want to use Azure SQL or Cosmos DB, continue to the appropriate link:

- [Getting Started with Data API builder for Azure SQL](./getting-started-azure-sql.md)
- [Getting Started with Data API builder for with Azure Cosmos DB](./getting-started-azure-cosmos-db.md)

And then proceed to the "Next Steps" to go even further

## Next Steps

### Running Data API Builder using Docker

You can use Docker to run the Data API Builder on your machine. Instructions are availabe here: [Running Data API Builder for Azure Databases using a container](../running-using-a-container.md)

### Using Data API Builder CLI to build the configuration file

Instead of creating the configuration file manually, you can take advantage of the CLI [Getting started with Data API Builder (`dab`) CLI](../getting-started/getting-started-dab-cli.md): 

### Deploy on Azure

Data API Builder can run in Azure so that you can easily build scalable applications. Detailed explanation - along with a sample script to help you getting started - is available here: [Running in Azure](./../running-in-azure.md)