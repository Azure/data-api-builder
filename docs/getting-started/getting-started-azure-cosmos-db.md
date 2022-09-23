# Getting started with Data API builder for Azure Cosmos DB

Make sure you have read the [Getting Started](getting-started.md) document.

This tutorial assumes that you have already a [Cosmos DB SQL API database account](https://learn.microsoft.com/azure/cosmos-db/sql/create-cosmosdb-resources-portal#create-an-azure-cosmos-db-account) that can be used as a playground.

## Create the database containers

Create the necessary database containers needed to represent Authors and Books.

-   `authors`: Collection containing authors with 'id' as the partition key
-   `books`: Collection containing books with 'id' as the partition key

Read more about [choosing partition key](https://docs.microsoft.com/en-us/azure/cosmos-db/partitioning-overview#choose-partitionkey) and [data modelling](https://docs.microsoft.com/en-us/azure/cosmos-db/sql/modeling-data)

Once the containers are created, you can import the sample data which are placed in the 'azure-cosmos-db' folder to the respective collections by using the [Data Import Tool](https://docs.microsoft.com/en-us/azure/cosmos-db/import-data#JSON).

## Add Book and Author schema files

We need to expose the books and the authors collections so that they can be used via GraphQL. Cosmos DB, being schema agnostic, requires us to provide the schema definition for the collections. These schema definitions need to be added in the `schema.gql` file.

Start by adding the `author` and `book` schema:

```graphql
type Author @model {
    id : ID,
    first_name : String,
    middle_name: String,
    last_name: String,
    Books: [Book]
}

type Book @model {
    id : ID,
    title : String,
    Authors: [String]
}
```

> **BEST PRACTICE**: It is recommended to use the *singular* form for entities names. For GraphQL, the Data API builder engine will automatically use the correct plural form to generate the final GraphQL schema whenever a *list* of entity items will be returned. More on this behavior in the [GraphQL documentation](./../graphql.md).

## Get the Cosmos DB Account connection string

You can obtain the connection string by navigating to your Azure Cosmos DB account page's key blade, and select Primary connection string. Copy the value to use in the Data API Builder.

![Cosmos DB connection string](../media/cosmos-connection.png)

You can also use Azure Cosmos DB emulator connection string if you are testing locally. The Azure Cosmos DB Emulator supports a [single fixed account and a well-known authentication key](https://learn.microsoft.com/azure/azure-sql/database/connect-query-content-reference-guide?view=azuresql)

The connection string looks like,

```
AccountEndpoint=AccountEndpoint=https://localhost:8081/;AccountKey=REPLACEME;
```

Now that you have all the required pieces in place, it's time to create the configuration file for DAB.

## Creating a configuration file for DAB

The Data API builder for Azure Databases engine needs a [configuration file](../configuration-file.md). There you'll define which database DAB connects to, and which entities are to be exposed by the API, together with their properties.

For this getting started guide you will use DAB CLI to initialize your configuration file. Run the following command:

```bash
dab init --database-type "cosmos" --graphql-schema schema.gql --cosmos-database PlaygroundDB --connection-string "AccountEndpoint=https://localhost:8081/;AccountKey=REPLACEME;" --host-mode "Development"
```

The command will generate a config file called `dab-config.json` looking like this:

```json
{
  "$schema": "dab.draft-01.schema.json",
  "data-source": {
    "database-type": "cosmos",
    "connection-string": "AccountEndpoint=https://localhost:8081/;AccountKey=REPLACEME;"
  },
  "cosmos": {
    "database": "PlaygroundDB",
    "schema": "schema.gql"
  },
  "runtime": {
    "rest": {
      "enabled": false,
      "path": "/api"
    },
    "graphql": {
      "allow-introspection": true,
      "enabled": true,
      "path": "/graphql"
    },
    "host": {
      "mode": "development",
      "cors": {
        "origins": [],
        "allow-credentials": false
      },
      "authentication": {
        "provider": "StaticWebApps"
      }
    }
  },
  "entities": {}
}
```

That's all you need at the moment. Let's work now on defining the entities exposed by the API.

## Add Book and Author entities

We want to expose the books and the authors collections so that they can be used via GraphQL. For doing that, all we need is to add the related information to the entities section of the configuration file.

> **NOTE**: REST operations are not supported for Cosmos DB via the
> Data API Builder, You can use the existing [REST API](https://learn.microsoft.com/rest/api/cosmos-db/)

Start by adding the `author` entity:

```json
 "entities": {
    "author": {
      "source": "authors",
      "rest": false,
      "graphql": true,
      "permissions": [
        {
          "role": "anonymous",
          "actions": [ "*" ]
        }
      ]
    }
```

within the `entities` object you can create any entity with any name (as long as it is valid for GraphQL). The name `author`, in this case, will be used to build the GraphQL type. Within the entity you have the `source` element that specifies which container contains the entity data. In our case it is `authors`.

> **NOTE**: Entities names are case sensitive and they will be exposed via GraphQL as you have typed them.

After that, you need to specify the permission for the exposed entity, so that you can be sure only those users making a request with the right claims will be able to access the entity and its data. In this getting started tutorial we're just allowing anyone, without the need to be authenticated, to perform all the CRUD operations to the `author` entity.

You can also add the `book` entity now, applying the same concepts you just learnt for the `author` entity. Once you have added the `author` entity, the `entities` object of configuration file will look like the following:

```json
 "entities": {
    "Author": {
      "source": "authors",
      "rest": false,
      "graphql": true,
      "permissions": [
        {
          "role": "anonymous",
          "actions": [ "*" ]
        }
      ]
    },
    "Book": {
      "source": "books",
      "rest": false,
      "graphql": true,
      "permissions": [
        {
          "role": "anonymous",
          "actions": [ "*" ]
        }
      ]
    }
  }
```

## Start Data API builder for Azure Cosmos DB

From the `samples/getting-started` folder, start Data API builder engine (use):

```
./run-dab.cmd library.config.json
```

Use `run-dab.sh` if you are on Linux. After a few seconds, you'll see something like

```
Now listening on: http://localhost:5000
Now listening on: https://localhost:5001
```

The Data API builder engine is running and is ready to accept requests.

## Query the endpoints

Now that the Data API builder engine is running, you can use your favorite REST client (Postman, Insomnia, etc.) to query the GraphQL endpoints.

### REST Endpoint

Unlike other databases, Data API Builder for Azure Cosmos DB does not support generating REST endpoints because there is already a[REST API endpoint](https://learn.microsoft.com/rest/api/cosmos-db/) capability built-in to the Azure Cosmos DB service.

### GraphQL endpoint

GraphQL endpoint is available at

```
/graphql
```

Use a GraphQL-capable REST client like Postman or Insomnia to query the database using full GraphQL introspection capabilities and to get IntelliSense and validation. For example:

```graphql
{
  books(first: 5, orderBy: { title: DESC }) {
    items {
      id
      title
    }
  }
}
```

will return the first five books ordered by title in descending order.

## GraphQL operations on entity relationships

With your GraphQL endpoint operational, you probably want to take advantage  of GraphQL's ability to handle complex requests.For example, you may want to get all the Books in your library along with the Authors they have written. In order to achieve that, you need to let Data API Builder know that you want that relationship to be available to be used in queries. We have defined the 
data models in such a way that they can be queried at once.

Using GraphQL you can now execute queries like:

```graphql
{
  books(filter: { title: { eq: "Foundation" } })
  {
    items {
      id
      title
      authors {
 
          first_name
          last_name
        
      }
    }
  }
}

```
This query will return List of books and its Authors.

Congratulations, you have just created a fully working backend to support your modern applications!
