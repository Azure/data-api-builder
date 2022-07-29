# Getting started with Data API builder for Azure Cosmos DB

Make sure you have read the [Getting Started](getting-started.md) document.

As mentioned before, this tutorial assumes that you have already a Cosmos DB database that can used as playground.

## Get the database connection string

## Add Book and Author entities


## Start Data API builder for Azure SQL Database

From the `sample` folder, where you should be already, start Data API builder engine (make sure you are using Powershell):

```
./run-dab.bat library.config.json
```

when you'll see something like

```
Now listening on: http://localhost:5000
Now listening on: https://localhost:5001
```

then the engine is running.

## Query the endpoints

Now that Data API builder engine is running, you can use your favourite REST client (Postman or Insomnia, for example) to query the REST or the GraphQL endpoints.

### REST Endpoint

REST endpoint is made available at the path:

```
/api/<entity>
```

so if you want to get a list of all the available books you can simply run this GET request:

```
/api/Book
```

The following HTTP verbs are supported:

- GET: return one or more items
- POST: create a new item
- PUT: update or create an item
- DELETE: delete an item

Whenver you need to access a single item, you can get the item you want by specifying its primary key:

```
GET /api/Book/id/1000
```

The ability to filter by primary key is supported by all verbs with the exception of POST as that verb is used to create a new iteam and therefore searching an item by its primary key is not applicable.

The GET verbs also support several query parameters that allows you to manipulate and refine the requested data:
- `$orderby`: defines how the returned data will be sorted
- `$first`: returns only the top `n` items
- `$filter`: filter the returned items
- `$select`: return only the selected columns

For more details on how they can be used, read refer to the [REST documentation](./docs/REST.md)

### GraphQL endpoint

GraphQL endpoint is available at

```
/graphql
```

Use a GraphQL-capable REST client like Postman or Insomina to query the database using full GraphQL introspection capabilities. For example:

```graphql
{
  books(first: 5) {
    items {
      id
      title
    }
  }
}
```
