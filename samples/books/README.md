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

```json
"author": {
  "source": "dbo.authors",
  "permissions": [{
    "role": "anonymous",
    "actions": [{
      "action": "*"
    }]
  }]
}
```

### Create a M:N relationship between Books and Authors

Between authors andf books entities there is a many-to-many relationship. To model such relationship the database contains a table named `` that connects books with their authors. Since this table is just used to sustain that relationship is not useful to expose it as a REST or GraphQL object. So the configuration will tell Hawaii that this table has to be used but not exposed.

The `book` entity needs to be updated using the following configuration:

```json
"book": {
  ...
  "relationships": {
    ...
    "authors": {
      "cardinality": "many",
      "linking.object": "dbo.books_authors",
      "target.entity": "author"
    }
  }
}
```

the above configuration it telling Hawaii that a `book` entity will have a `authors` property thjat will connected it to an `author` entity with a cardinalit of `many` and that such relatioship is implemented via `dbo.book_authors` in the underlying database. A sample of the GraphQL that can be used to query books and related authors is:

```graphql
{
  books {
    items {
      id
      title
      authors {
        items {
          full_name
        }
      }
    }
  }
}
```

Of course, also the `author` entity needs to be updated, to allow navigation from authors to the books they have written. The `relationship` section of the `author` entity must be changed by adding the relationship with `book` entity, similarly to what was done before:

```json
"author": {
  ...
  "relationships": {
    ...
    "books": {
      "cardinality": "many",
      "linking.object": "dbo.books_authors",
      "target.entity": "book"
    }
  }
}
```

### Enrich the relatioship between Books and Authors
