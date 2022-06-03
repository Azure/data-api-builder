# Books Catalog Sample

TDB: Describe the sample scenario

## Backend Database

This sample uses Azure SQL DB or SQL Server Sample as the backed database. Use the `book.sql` file to create the database objects used by the sample.

## Hawaii Configuration

TDB

### Add a Book entity

The `dbo.books` table needs to be exposed as a REST and GraphQL endpoint. To do that you have to create a `book` entity in the `entity` section of the configuration file:

```json
"book": {
    "source": "dbo.books",
    "permissions": [{
        "role": "anonymous",
        "actions": [ "*" ]
    }]
}
```

The above configuration, will tell Hawaii that the anyone, even those request which cannot be authenticated, will be allowed to perform all the CRUD actions on the `book` entity.

The same configuration can also be defined via CLI, instead of writing the JSON manually:

TDB

The `book` entity will be available as a REST endpoint at `/api/book`:

```sh
curl -X GET http://localhost:5000/api/book
```

or as GraphQL object at the `/graphql` endpoint:

```graphql
{
	books {
		items {
			id
			title
		}
	}
}
```

### Add a Category entity

Each book will be able to be categorized into one category, so a category entity is needed. The database contains the `dbo.categories` table that can be exposed by adding the following `category` entity to the configuration file:

```json
"category": {
    "source": "dbo.categories",
    "permissions": [{
        "role": "anonymous",
        "actions": [ "*" ]
    }]
}
```

The same configuration can also be defined via CLI, instead of writing the JSON manually:

TDB

The `category` entity will be available as a REST endpoint at `/api/category`:

```sh
curl -X GET http://localhost:5000/api/category
```

or as GraphQL object at the `/graphql` endpoint:

```graphql
{
	categories {
		items {
			id
			title
		}
	}
}
```

### Create a 1:N relationship between Categories and Books

As a book can be categorized using one of the available category, we want to surface that one-to-many relationship between categories and books (and, vice-versa, a many-to-one relationship between books and categories) so that it will be usable to build GraphQL requests. To do that you we need to inform Hawaii that we want to take advantage of the existing Foreign Key. Thanks to the Foreign Key only the fact that we want to expose that relationship need to be configured as Hawaii can figure how to correctly correlate `books` and `categories` entity by reading the existing metadata.

To add the one-to-many relatioship between categories and books, update the `category` entity by adding the `relationship` element:

```json
"category": {
    "source": "dbo.categories",
    "relationships": {
        "book": {
            "cardinality": "many",
            "target.entity": "book"
        }
    },
    "permissions": [{
        "role": "anonymous",
        "actions": [ "*" ]
    }]
}
```

in the same way, the `book` entity needs to be updated:

```json
"book": {
    "source": "dbo.books",
    "relationships": {
        "category": {
            "cardinality": "one",
            "target.entity": "category"
        }
    },
    "permissions": [{
        "role": "anonymous",
        "actions": [ "*" ]
    }]
}
```

as you notice in the configuration file there are only the information needs to tell Hawaii what are the entities taking part in the relationship and the cardinality of such relationship. How the relationship is implemented behind the scenes is automatically inferred from the existing Foreign Key. If there are no Foreign Keys or there are ambiguities as there are many Foreign Keys that can potentilly be used, you can specifiy the databases fields that will be used to sustain the relationship using the `source.fields` and `target.fields` elements.

Once the configuration has been updated, you can navigate the relationships via GraphQL, for example:

```graphql
{
	books {
		items {
			id
			title,
			category {
				category
			}
		}
	}
}
```

### Add an Author entity

Books are written by authors, and therefore we need to expose the `author` entity in order to allow developers to manage authors:

```
```

### Create a M:N relationship between Books and Authors

### Enrich the relatioship between Books and Authors
