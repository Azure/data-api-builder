# Getting started with Data API builder for Azure SQL Database

Make sure you have read the [Getting Started](getting-started.md) document.

As mentioned before, this tutorial assumes that you already have a SQL Server or an Azure SQL database that can be used as playground.

## Get the database connection string

There are several ways to get an Azure SQL database connection string. More details here:
https://docs.microsoft.com/en-us/azure/azure-sql/database/connect-query-content-reference-guide?view=azuresql

If you are connecting to Azure SQL DB, Azure SQL MI, or SQL Server, the connection string look like:

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

if you haven't created your configuration file yet, you can do it right now, for example, below command will generate a config with name `dab-config.json`, use --config otherwise to specify the name:

```bash
dab init --database-type "mssql" --connection-string "Server=localhost;Database=PlaygroundDB;User ID=PlaygroundUser;Password=<Password>;TrustServerCertificate=true"
```

if you already have created the configuration file, you can just open it with an editor like VS Code and manually update the `connection-string` and the `database-type` in the `data-source` section.

## Create the database objects

Create the database tables needed to represent Authors, Books and the many-to-many relationship between Authors and Books. You can find the `libray.azure-sql.sql` script in the `azure-sql-db` folder in the GitHub repo. You can use it to create three tables, along with sample data:

- `dbo.authors`: Table containing authors
- `dbo.books`: Table containing books
- `dbo.books_authors`: Table associating books with respective authors

Execute the script in the SQL Server or Azure SQL database you decided to use, so that the tables with samples data are created and populated.

## Add Book and Author entities

Now, you'll want to expose the `books` and the `authors` table as REST or GraphQL endpoints. To do that, add the following information to the `entities` section of the configuration file.

You can do this either using the CLI:

```bash
dab add author --source dbo.authors --permissions "anonymous:*"
```

or by adding the `author` entity manually to the config file:

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

> **NOTE**: Entities names are case sensitive, and they will be exposed via REST and GraphQL as you have typed them.

After that, the permissions for the exposed entity are defined via the `permission` element; it allows you to be sure that only those users making a request with the right claims will be able to access the entity and its data. In this getting started tutorial, we're allowing anyone, without the need to be authenticated, to perform all the CRUD operations to the `author` entity.

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

You are ready to serve your API. Run the below command (this will start the engine with default config `dab-config.json`, use option --config otherwise):

```
dab start
```

when you'll see something like:

```
info: Azure.DataApiBuilder.Service.Startup[0]
      Successfully completed runtime initialization.
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5001
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

you'll be good to go, Data API Builder is up and running, ready to serve your requests.

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

- `$orderby`: return items in the specified order
- `$first`: the top `n` items to return
- `$filter`: expression to filter the returned items
- `$select`:  list of field names to be returned
 
For more details on how they can be used, refer to the [REST documentation](../rest.md)

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

Everything is now up and working, and now you probably want to take advantage as much as possible of GraphQL capabilities to handle complex queries by sending just one request. For example you may want to get all the Authors in your library along with the Books they have written. In order to achieve that you need to let Data API Builder know that you want such relationship to be available to be used in queries.

Stop the engine (`Ctrl+C`).

Relationships are also defined in the configuration file, via the `relationships` section. Relationships must be defined on each entity where you want to have them. For example to create a relationshop between a Book and its Authors, you can use the following DAB CLI command:

```bash
dab update author --relationship "books" --cardinality "many" --target.entity "book" --linking.object "dbo.books_authors"
```

which will create the `relationships` section in the `author` entity:

```json
"relationships": {
    "books": {
        "cardinality": "many",
        "target.entity": "book",
        "linking.object": "dbo.books_authors"
    }
}
```

The element under `relationship` is used to add a field - `books` in the sample - to the generated GraphQL object, so that one will be able to navigate the relationship between an Author and their Books. Within the `books` object there are three fields:

- `cardinality`: set to `many` as an author can be associated with more than one book
- `target.entity`: Which entity, defined in the same configuration file, will be used in this relationship. For this sample is `book` as we are creating the relationship on the `author` entity.
- `linking.object`: the database table used to support the many-to-many relationship. That table is the `dbo.books_authors`.

Data API Builder will automatically figure out what are the columns that are used to support the relationship between all the involved parts by analyzing the forieng keys constratins that exist between the involved tables. For this reason the configuration is done! (If you don't have foreign keys you can always manually specify the columns you want to use to navigate from one table to another. More on this in the [relationships documentation](../relationships.md))

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

as we also want to enable querying a book and getting its authors, we also need to make a similar change to the book entity:

```bash
```

that will update the configuration file so that the `book` entity will look like the following code:

```json
"book": {
    "source": "dbo.books",
    "permissions": [
        {
            "actions": [ "*" ],
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

Once this is done, you can now restart the Data API builder engine, and using GraphQL you can now execute queries like:

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

## Exercise

If you want to practice what you have learned, here's a little exercise you can do on your own

- Using the database setup script `/samples/getting-started/azure-sql-db/exercise/exercise.library.azure-sql.sql`:
  - add the table `dbo.series` which will store series names (for example: [Foundation Series](https://en.wikipedia.org/wiki/Foundation_series))
  - update the `dbo.books` table by adding a column named `series_id`
  - update the `dbo.books` table by adding a foreign key constraint on the `dbo.series` table
- Update the configuration file with a new entity named `series`, supported by the `dbo.series` source table you just created.
- Update the `book` entity by creating a relationship with the `series` entity. Make sure you select `one` for the `cardinality` property
- Update the `series` entity by creating a relationship with the `book` entity. Make sure you select `many` for the `cardinality` property