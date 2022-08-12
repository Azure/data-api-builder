# Getting started with Data API builder for Azure Databases

Welcome to this getting started tutorial that will guide you to have Data API builder for Azure Databases running locally on your machine as a first step, and then will guide you to deploy Data API builder for Azure Databases in an Azure Container Instance, so that at the end of the tutorial you'll have full knowledge of what's needed to run Data API builder for Azure Databases both on-prem and in Azure.

## Prerequisites

### .NET 6.0
Make sure you have .NET 6.0 SDK installed on your machine: https://dotnet.microsoft.com/en-us/download/dotnet/6.0.

### Azure Database

As the Data API builder for Azure Databases generate REST and GraphQL endpoints for database objects, you need to have a database ready to be used. You can choose either a relational or non-relational database. The getting started guide document the process to have Data API builder set up for:
- Azure SQL Database
- Azure Cosmos DB

### Git

Please note that familiarity with Git commands, tools and concept is assumed throughout all the tutorial.


## Clone the Data API builder for Azure Databases engine

Clone the repository locally:

```
git clone https://github.com/Azure/hawaii-engine.git
```

then move into the folder where the Data API builder for Azure Databases engine has been cloned and then move into the `samples/getting-started` folder, for example:

```
cd ./hawaii-engine/samples/getting-started
```

## Create the configuration file

The Data API builder for Azure Databases engine needs a [configuration file](../configuration-file.md) to know to which database it has to connect to, and what are the entities that have to be exposed, and their properties.

Creating a configuration file is simple and you can use the Data API builder CLI to make it even simpler. In this tutorial the CLI will not be used so that you can get the chance to get familar with the configuration file, as it is a key part of Data API builder for Azure Databases.

Create a copy of the `basic-empty-dab-config.json` file and rename it `library-dab-config.json`

The content of the file is the following:

```json
{
    "$schema": "../schemas/hawaii.draft-01.schema.json",
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

Aside the `$schema` property that points to the JSON schema that can be used to validate the configuration file, and that will be used by IDE like Visual Studio Code to provide intellisense and autocomplete, there are a few other properties that you need to know.

In the `data-source` section you have to specify the database type and the connection string to connect to the database containing the objects you want to expose.

`database-type` can be any of the following:
- `mssql`: for Azure SQL DB, Azure SQL MI or SQL Server
- `cosmos`: for Azure Cosmos DB (SQL API)
- `postgresql`: for PostgreSQL
- `mariadb`: for MariaDB
- `mysql`: for MySQL

Set the value of `database-type` to the database you plan to use. For this tutorial we'll be using `mssql` or `cosmos` to showcase both the relational and non-relational support.

Once you have chosen the database you want to connect to, you need to provide the connection string in the `connection-string` property. This is a standard ADO.NET connection string for the database you'll be using and you can get it from the Azure Portal, for example. If don't know the connection string, no worries, we'll take care of that later.

The `runtime` section is telling Data API builder to run in `development` mode. This means database errors will be surfaced and returned with full detail. This is great for development, but can be a security risk when running in production: that's why switching to `production` mode will disable this ability.

The remaining property is the `entities` property and it is empty for now. This property will contain all the object you want to be exposed as REST or GraphQL endpoints.

## The sample scenario

In this tutorial we'll be creating the backend API for a small solution that allow end-user to keep track of the book in their library. Therefore the business entities we'll be dealing with are

- Books
- Authors

Both the business entities need a modern endpoint, REST and/or GraphQL, to allow third party developers to build mobile and desktop application to manage the library catalog. Data API builder is perfect for enabling that modern endpoint support.

## Configure the Entities

Depending if you want to use Azure SQL Database or Cosmos DB, continue to the appropriate link:

- [Getting Started with Data API builder for Azure SQL DB](./getting-started-azure-sql-db.md)
- [Getting Started with Data API builder for with Azure Cosmos DB](./getting-started-azure-cosmos-db.md)

