# Getting started with Data API builder for Azure Databases

Welcome to this getting started tutorial that will guide you to have Data API builder for Azure Databases running locally on your machine as a first step and then will guide you to deploy Data API builder for Azure Databases in an Azure Container Instance, so that at the end of the tutorial you'll have full knowledge of what's needed to run Data API builder for Azure Databases both on-prem and in Azure.

## Pre-Requisite

As the Data API builder for Azure Databases generate REST and GraphQL endpoints for database objects, you need to have a database ready to be used. You can choose either a relational or non-relational database.

Please note that familiarity with Git commands and concept is assumed throughout all the tutorial

## Clone the Data API builder for Azure Databases engine

Clone the repository locally

```
git clone https://github.com/Azure/hawaii-engine.git
```

then move into the folder where the Data API builder for Azure Databases engine has been clone and then move into the `samples` folder, for example:

```
cd ./hawaii-engine/samples
```

## Create the configuration file

Data API builder for Azure Databases engine needs a configuration file to know to which database it has to connect to, and what are the entities that have to be exposed, and their properties.

Creating a configuration file is simple and you can use the Data API builder CLI to make it even simpler. In this tutorial the CLI will not be used so that you can the chance to get familar with the configuration file, as it is a key part of Data API builder for Azure Databases.

Create a copy of the `basic-empty.config.json.sample` file and rename it `library.config.json`

The content of the file is the following:

```json
{
    "$schema": "../schemas/hawaii.draft-01.schema.json",
    "data-source": {
        "database-type": "",
        "connection-string": ""
    },
    "entities": {
    }
}
```

Aside the the `$schema` property that points to the JSON schema that can be used to validate the configuration file, and that will be used by IDE like Visual Studio Code to provide intellisense and autocomplete, there are a few other properties that you need to know.

In the `data-source` section you to specifiy the database type and the connection string to connect to the database containing the objects you want to expose.

`database-type` can be any of the following:
- `mssql`: for Azure SQL DB, Azure SQL MI or SQL Server
- `cosmosdb`: for Azure Cosmos DB
- `postgresql`: for PostgreSQL
- `mariadb`: for MariaDB
- `mysql`: for MySQL

Set the value of `database-type` to the database you plan to use. For this tutorial we'll be using `mssql` or `cosmosdb` to showcase both the relational and non-relational support.

Once you have chosen the database you want to connect to, you need to provide the connection strin gin the `connection-string` property. This is a standard ADO.NET connection string for the database you'll be using and you can get from the Azure Portal, for example. If don't know the connection string, no worries, we'll take care of that later.

The remaining property is the `entities` property and it is empty for now. This propety will contain all the object you want to be exposed as REST or GraphQL endpoints.

## The sample scenario

In this tutorial we'll be creating the backend API for a small solution that allow end-user to keep track of the book in their library. Therefore the business entities we'll be dealing with are

- Books
- Authors

Both the business entities need a modern endpoint, REST and/or GraphQL, to allow third party developers to build mobile and desktop application to manage the library catalog.

## Configure the Entities

Depending if you want to use Azure SQL Database or Cosmos DB, continue to the appropriate link:

- [Getting Started with Data API builder for Azure SQL DB](./getting-started-azure-sql-db.md)
- [Getting Started Data API builder for with Azure Cosmos DB](./getting-started-azure-cosmos-db.md)

