# Getting started with Data API builder for Azure SQL Database

Make sure you have read the [Getting Started](getting-started.md) document.

As mentioned before, this tutorial assumes that you already have a SQL Server or an Azure SQL database that can be used as playground.

## Get the database connection string

The are several ways to get an Azure SQL database connection string. More details here:
https://docs.microsoft.com/en-us/azure/azure-sql/database/connect-query-content-reference-guide?view=azuresql

Both if you are connectiong to Azure SQL DB or Azure SQL MI or SQL Server, the connection string look like:

```
Server=<server-address>;Database=<database-name>;User ID=<user-d>;Password=<password>;
```

To connect to a local SQL Server, for example:

```
Server=localhost;Database=Library;User ID=dab_user;Password=<password>;TrustServerCertificate=true
```

More details on Azure SQL and SQL Server connection strings can be found here: https://docs.microsoft.com/en-us/sql/connect/ado-net/connection-string-syntax

Once you have your connection string, add it to the configuration file you have created before. It will look like the following (if you are using a local SQL Server):

```json
"data-source": {
    "database-type": "mssql",
    "connection-string": "Server=localhost;Database=PlaygroundDB;User ID=PlaygroundUser;Password=<Password>;TrustServerCertificate=true"
}
```

## Create the database objects

Create the database tables needed to represent Authors, Books and the many-to-many relationship between Authors and Books. You can find the `libray.azure-sql.sql` script in the 'azure-sql-db' folder that you can use to create three tables, along with sample data:

- `dbo.authors`: Table containing authors
- `dbo.books`: Table containing books
- `dbo.books_authors`: Table associating books with respective authors

Execute the script in the SQL Server or Azure SQL database you decided to use, so that the tables with samples data are created and populated.

## Add Book and Author entities

Now, you'll want to expose the `books` and the `authors` table as REST or GraphQL endpoints. To do that, add the following information to the `entities` section of the configuration file.

Start by adding the `author` entity:

```json
"entities": {
    "author": {
      "source": "dbo.authors",
      "permissions": [
        {
          "actions": ["*"],
          "role": "anonymous"
        }
      ]
    }
}
```

within the `entities` object you can create any entity with any name (as long as it is valid for REST and GraphQL). The name `author`, in this case, will be used to build the REST path and the GraphQL type. Within the entity you have the `source` element that specifies which table contains the entity data. In our case is `dbo.authors`.

> **NOTE**: Entities names are case sensitive and they will be exposed via REST and GraphQL as you have typed them.

After that, you need to specify the permission for the exposed entity, so that you can be sure only those users making a request with the right claims will be able to access the entity and its data. In this getting started tutorial we're just allowing anyone, without the need to be authenticated, to perform all the CRUD operations to the `author` entity.

You can also add the `book` entity now, applying the same concepts you just learnt for the `author` entity. Once you have added the `author` entity, the `entities` object of configuration file will look like the following:

```json
"entities": {
    "author": {
      "source": "dbo.authors",
      "permissions": [
        {
          "actions": ["*"],
          "role": "anonymous"
        }
      ]
    },
    "book": {
      "source": "dbo.books",
      "permissions": [
        {
          "actions": ["*"],
          "role": "anonymous"
        }
      ]
    }
  }
```

that's all is needed at the moment. Data API builder is ready to be run.

> **BEST PRACTICE**: It is recommeneded to use the *singular* form for entities names. For GraphQL, the Data API builder engine will automatically use the correct plural form to generate the final GraphQL schema whenever a *list* of entity items will be returned. More on this behaviour in the [GraphQL documentation](./../graphql.md).

## Start Data API builder for Azure SQL Database

From the `samples/getting-started` folder, start Data API Builder engine :

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

Now that Data API builder engine is running, you can use your favourite REST client (Postman or Insomnia, for example) to query the REST or the GraphQL endpoints.

### REST Endpoint

REST endpoint is made available at the path (make sure to keep in mind that the url path is treated as Case Sensitive):

```
/api/<entity>
```

so if you want to get a list of all the available books you can simply run this GET request:

```
/api/book
```

The following HTTP verbs are supported:

- `GET`: return one or more items
- `POST`: create a new item
- `PUT` `PATCH`: update or create an item
- `DELETE`: delete an item

Whenver you need to access a single item, you can get the item you want by specifying its primary key:

```
GET /api/book/id/1000
```

The ability to filter by primary key is supported by all verbs with the exception of POST as that verb is used to create a new item and therefore searching an item by its primary key is not applicable.

The GET verb also supports several query parameters that allow you to manipulate and refine the requested data:
- `$orderby`: defines how the returned data will be sorted
- `$first`: returns only the top `n` items
- `$filter`: filters the returned items
- `$select`: returns only the selected columns

For more details on how they can be used, refer to the [REST documentation](./docs/REST.md)

### GraphQL endpoint

GraphQL endpoint is available at

```
/graphql
```

Use a GraphQL-capable REST client like Postman or Insomnia to query the database using full GraphQL introspection capabilities, to get intellisense and validation. For example:

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

## Adding entities relationships

Everything up and working, and now you probably want to take advantage as much as possible of GraphQL capabilities to handle complex request in just one request. For example you may want to get all the Authors in your library along with the Books they have written. In order to achieve that you need to let Data API Builder know that you want that relationship to be available to be used in queries.

Stop the engine and go back to the `library.config.json` and add the `relationships` section to the `author` entity, using the code below:

```json
"relationships": {
    "books": {
        "cardinality": "many",
        "target.entity": "book",
        "linking.object": "dbo.books_authors"
    }
}
```

The element under `relationship` is used to add a field - `books` in the sample - to the generated GraphQL object, so that one will be able to navigate the relationship between an Author and its Books. Within the `books` object there are three fields:

- `cardinality`: set to `many` as an author can be associated with more than one book
- `target.entity`: Which entity, defined in the same configuration file, will be used in this relationship. For this sample is `book` as we are creating the relationship on the `author` entity.
- `linking.object`: the database table used to support the many-to-many relationship. That table is the `dbo.books_authors`.

Data API Builder will automatically figure out what are the columns that are used to support the relationship between all the involved parts by analyzing the forieng keys constratins that exist between the involved tables. For this reason the configuration is done! (If you don't have foreign keys you can always manually specify the columns you want to use to navigate from one table to another. More on this in the [relationships documentation](relationships.md))

The `author` entity should now look like the following:

```json
"author": {
    "source": "dbo.authors",
    "permissions": [
        {
            "actions": [ "*" ],
            "role": "anonymous"
        }
    ],
    "relationships": {
        "books": {
            "cardinality": "many",
            "target.entity": "book",
            "linking.object": "dbo.books_authors"
        }
    }
},
```

as we also want to enable quering a Book and getting its authors, we need to also make a similar change to the Book entity:

```json
"book": {
    "source": "dbo.books",
    "permissions": [
        {
            "actions": [
            "*"
            ],
            "role": "anonymous"
        }
    ],
    "relationships": {
        "authors": {
            "cardinality": "many",
            "target.entity": "author",
            "linking.object": "dbo.books_authors"
        }
    }
}
```

Done, you can now restart the Data API builder engine, and using GraphQL you can now execute queries like:

```graphql
{
  books(filter: { title: { eq: "Nightfall" } })
  {
    items {
      id
      title
      authors {
        items {
          first_name
          last_name
        }
      }
    }
  }
}
```

that will return all the authors of "Nightfall" book, or like:

```graphql
{
  authors(
    filter: {
        or: [
          { first_name: { eq: "Isaac" } }
          { last_name: { eq: "Asimov" } }
        ]
    }
  ) {
    items {
      first_name
      last_name
      books {
        items {
          title
        }
      }
    }
  }
}
```

that will return all the books written by Isaac Asimov.

Congratulations, you have just created a fully working backend to support your modern applications!

## Deploy on Azure

WIP

## Conclusion

WIP


