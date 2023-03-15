# Getting started with Data API builder for Azure MySQL Database

Make sure you have read the [Getting Started](getting-started.md) document. As mentioned before, this tutorial assumes that you already have an Azure MySQL Database that can be used as a playground.

## Get the database connection string

There are several ways to get an Azure MySQL Database connection string. See [how to connect and query with MySQL](https://learn.microsoft.com/azure/mysql/flexible-server/connect-csharp).

For Data API builder, the format used for a MySQL connection is shown below based on SSL configuration:

1. If MySQL server has SSL enabled, use the ADO.NET connection string format with SSL mode as required. If using an Azure MySQL Database, remember to download and install the [public SSL certificate](https://dl.cacerts.digicert.com/DigiCertGlobalRootCA.crt.pem) in the **Trusted Root certification authorities store** on the client machine using **certmgr.msc** Management Console on your local Windows system. If using an Azure cloud service like Azure App Service, you can copy the certificate to a folder on the App Service file system and add the argument **SslCa** using the full certificate path as shown below.

   ```
   Server=<server-address>;Database=<database-name>;User ID=<username>;Password=<password>;Sslmode=Required;SslCa=<path-to-certificate>";
   ```

2. If MySQL does not have SSL enabled, you can use the ADO.NET connection string format without the SSL mode parameter
   ```
   Server=<server-address>;Database=<database-name>;User ID=<username>;Password=<password>;
   ```

## Create the database objects

Create the database `booksdb` with tables to represent Authors, Books and the many-to-many relationship between Authors and Books. Execute this [sample script for books schema and data](../../samples/getting-started/azure-mysql-db/library.azure-mysql.sql) in the MySQL Database you decided to use.

- `authors`: Table containing authors
- `books`: Table containing books
- `books_authors`: Table associating books with respective authors

## Creating a configuration file for DAB

The Data API builder engine needs a [configuration file](../configuration-file.md). There, you'll define which database DAB targets, and which entities and associated properties are to be exposed by the API.

For this getting started guide, you will use DAB CLI to initialize your configuration file. Run the following command and use the connection string based on whether SSL is enabled or not. Please review the **Get the database connection string** section above.

```bash
dab init  --config "dab-config.MySql.json" --database-type mysql --connection-string "<mysql-connection-string-ssl-or-non-ssl>" --host-mode "Development" --authenticate-devmode-requests false --cors-origin "http://localhost:5000"
```

The command will generate a config file called dab-config.MySql.json looking like this:

```json
{
  "$schema": "https://dataapibuilder.azureedge.net/schemas/v{dab-version}/dab.draft.schema.json",
  "data-source": {
    "database-type": "mysql",
    "connection-string": "Server=demoazuredbmysql.mysql.database.azure.com;User ID=dbusername;Password=<Password>;database=<dbname>;Sslmode=Required;SslCa=<path-to-SSLcert>"
  },
  ,
  "runtime": {
    "rest": {
      "enabled": true,
      "path": "/api"
    },
    "graphql": {
      "enabled": true,
      "allow-introspection": true,
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
````

As you can see there the `data-source` property specifies that our chosen `database-type` is `mysql`, with the `connection-string` we passed to DAB CLI.

> Take a look at the [DAB Configuration File Guide](../configuration-file.md) document to learn more. With the configuration file in place, then it's time to start defining which entities you want to expose via the API.

## Add Book entities

Now, you'll want to expose the `books` table as REST and/or GraphQL endpoints.

Run the DAB CLI command as shown below to create the entity called Books

```
    dab add Book --config "dab-config.MySql.json" --source books --permissions "anonymous:create,read,update,delete"
```

This will add the following information to the `entities` section of the configuration file.

```json
"entities": {
    "Book": {
      "source": "books",
      "permissions": [
        {
          "role": "anonymous",
          "actions": [
            "create",
            "read",
            "update",
            "delete"
          ]
        }
      ]
    }
}
```

within the `entities` object you can create any entity with any name (as long as it is valid for REST and GraphQL). The name `Book`, in this case, will be used to build the REST path and the GraphQL type. Within the entity you have the `source` element that specifies which table contains the entity data. In our case is `books`. You can add other entities for `authors` table and `books_authors` table using `dab add` CLI command.

> **NOTE**: Entities names are case sensitive, and they will be exposed via REST and GraphQL as you have typed them.

> **BEST PRACTICE**: It is recommeneded to use the _singular_ form for entities names. For GraphQL, the Data API builder engine will automatically use the correct plural form to generate the final GraphQL schema whenever a _list_ of entity items will be returned. More on this behaviour in the [GraphQL documentation](./../graphql.md).

After that, the permissions for the exposed entity are defined via the `permissions` element; it ensures that only those users making a request with the right claims will be able to access the entity and its data. In this getting started tutorial, we're allowing anyone, without the need to be authenticated, to perform all the CRUD operations to the `Book` entity.

You can also add the `Author` entity now, applying the same concepts you just learnt for the `Book` entity. Once you have added the `Author` entity, the `entities` object of configuration file will look like the following:

```json
"entities": {
    "Author": {
      "source": "authors",
      "permissions": [
        {
          "actions": ["*"],
          "role": "anonymous"
        }
      ]
    },
    "Book": {
      "source": "books",
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

## Start Data API builder for Azure MySQL Database

To start the DAB API builder with the configuration file, run the following command:

```
    dab start -c dab-config.MySql.json
```

The output would look like

```
info: Azure.DataApiBuilder.Service.Services.ISqlMetadataProvider[0]
      Book path: /api/Book
info: Azure.DataApiBuilder.Service.Configurations.RuntimeConfigValidator[0]
      Validating Relationship Section in Config...
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5001
```

The Data API builder engine is running and is ready to accept requests.

## Query the endpoints

Now that Data API builder engine is running, you can use your favourite API client (Postman or Insomnia, for example) to query the REST or the GraphQL endpoints.

### REST Endpoint

The REST endpoint is made available at the path (make sure to keep in mind that the url path is treated as Case Sensitive):

```
/api/<entity>
```

so if you want to get a list of all the available books you can simply run this GET request:

```
/api/Book
```

The following HTTP verbs are supported:

- `GET`: return one or more items
- `POST`: create a new item
- `PUT` `PATCH`: update or create an item
- `DELETE`: delete an item

Whenever you need to access a single item, you can get the item you want by specifying its primary key:

```
GET /api/Book/id/1000
```

The ability to filter by primary key is supported by all verbs with the exception of POST as that verb is used to create a new item and therefore searching an item by its primary key is not applicable.

The GET verb also supports several query parameters (also case sensitive) that allow you to manipulate and refine the requested data:

- `$orderby`: return items in the specified order
- `$first`: the top `n` items to return
- `$filter`: expression to filter the returned items
- `$select`: list of field names to be returned

For more details on how they can be used, refer to the [REST documentation](../rest.md)

### GraphQL endpoint

GraphQL endpoint is available at

```
/graphql
```

Use a GraphQL-capable API client like Postman or Insomnia to query the database using full GraphQL introspection capabilities, to get IntelliSense and validation. For example:

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

Everything is now up and working, and now you probably want to take advantage as much as possible of GraphQL capabilities to handle complex queries by sending just one request. For example you may want to get all the Authors in your library along with the books they have written. In order to achieve that you need to let Data API builder know that you want such relationship to be available to be used in queries.

Stop the engine (`Ctrl+C`). Relationships must be defined on each entity where you want to have them. For example to create a relationship between a Book and its Authors, you can use the following DAB CLI command:

```
    dab update Author --relationship "books" --cardinality "many" --target.entity "Book" --linking.object "dbo.books_authors"
```

which will create the relationships section in the `Author` entity:

```json
"relationships": {
    "books": {
        "cardinality": "many",
        "target.entity": "Book",
        "linking.object": "books_authors"
    }
}
```

The element under `relationship` is used to add a field - `books` in the sample - to the generated GraphQL object, so that one will be able to navigate the relationship between an Author and their books. Within the `books` object there are three fields:

- `cardinality`: set to `many` as an author can be associated with more than one book
- `target.entity`: Which entity, defined in the same configuration file, will be used in this relationship. For this sample, it is `Book` as we are creating the relationship on the `Author` entity.
- `linking.object`: the database table used to support the many-to-many relationship. That table is the `books_authors`. If you are creating a simple One-to-Many or Many-to-One relationship, this field is not needed.

Data API builder will automatically figure out which columns are used to support the relationship between all the involved parts by analyzing the foreign key constraints that exist between the involved tables. For this reason, the configuration is done! (If you don't have foreign keys, you can always manually specify the columns you want to use to navigate between table relationships. More on this in the [relationships documentation](../relationships.md))

The `Author` entity should now look like the following:

```json
"Author": {
    "source": "authors",
    "permissions": [
        {
            "actions": [ "*" ],
            "role": "anonymous"
        }
    ],
    "relationships": {
        "books": {
            "cardinality": "many",
            "target.entity": "Book",
            "linking.object": "books_authors"
        }
    }
},
```

as we also want to enable querying a book and getting its authors, we also need to make a similar change to the `Book` entity:

```json
"Book": {
    "source": "books",
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
            "target.entity": "Author",
            "linking.object": "books_authors"
        }
    }
}
```

Once this is done, you can restart the Data API builder engine. Then, you can execute GraphQL queries like:

```graphql
{
  books(filter: { title: { eq: "Nightfall" } }) {
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
      or: [{ first_name: { eq: "Isaac" } }, { last_name: { eq: "Asimov" } }]
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
