# GraphQL in Data API builder

Entities configured to be available via GraphQL will be available at the path 

```
http://<dab-server>/graphql
```

Data API Builder will automatically generate a GraphQL schema with query and mutation support for all configured entities. GraphQL schema can be explored using a modern GraphQL client like [Insomnia](http://insomnia.rest/) or [Postman](https://www.postman.com/), so that you'll have Intellisense and Autocomplete.

If you followed the [Getting Started](./getting-started/getting-started.md) example, where there are the `books` and the `authors` entity configured for GraphQL access, you can see how easy is to use GraphQL.

## Resultset format

The returned result is a JSON object with this format:

```json
{
    "data": {}    
}
```

> **Attention!**: Only the first 100 items are returned by default.

## Supported GraphQL Root Types

Data API Builder support the following GraphQL root types:

[Queries](#queries)
[Mutations](#mutations)

## Queries

Each entity has support the following actions:

- [Pagination](#pagination)
- [Query by Primary key](#query-by-primary-key)
- [Generic Query](#generic-query)

Data API Builder, unless otherwise specified, will use the *singular* name of an entity whenever a single item is expected to be returned, and will use the *plural* name of an entity whenever a list of items is expected to be returned. For example the `book` entity will have:

- `book_by_pk()`: to return zero or one entity 
- `books()`: to return a list of zero or more entities

### Pagination

All query types returing zero or more items supports pagination: 

```graphql
{
  books
  {
    items {
      title
    }
    hasNextPage
    endCursor
  }
}
```

- the `item` object allows access to entity fields
- `hasNextPage` is set to true if there are more items to be returned
- `endCursor` returns an opaque cursor string that can be used with [`first` and `after`](#first-and-after) query parameters to get the next set (or page) of items.

### Query by Primary key

Every entity support retrieval of a specific item via its Primary Key, using the following query format:

```
<entity>_by_pk(<pk_colum>:<pk_value>)
{
    <fields>
}
```

for example:

```graphql
{
  book_by_pk(id:1010) {
    title
  }
}
```

### Generic Query

Every entity also support a generic query pattern so that you can ask for  only the items you want, in the order you want, using the following parameters:

- [`filter`](#filter): filters the returned items
- [`orderBy`](#orderBy): defines how the returned data will be sorted
- [`first` and `after`](#first-and-after): returns only the top `n` items

for example: 

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
      books(orderBy: { year: ASC }) {
        items {
          title
          year
        }
      }
    }
  }
}
```

### `filter`

The value of the `filter` parameter is predicate expression (an expression that returns a boolean value) using entity's fields. Only items where the expression evaluates to true are included in the response. For example:

```graphql
{
  books(filter: { title: { contains: "Foundation" } })
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

all the books with the word `Foundation` in the title will be returned.

The operators supported by the `filter` parameter are:

Operator                 | Description           | Example
--------------------     | --------------------- | -----------------------------------------------------
**Comparison Operators** |                       |
eq                       | Equal                 | `books(filter: { title: { eq: "Foundation" } })`
neq                      | Not equal             | `books(filter: { title: { neq: "Foundation" } })`
gt                       | Greater than          | `books(filter: { year: { gt: 1990 } })`
gte                      | Greater than or equal | `books(filter: { year: { gte: 1990 } })`
lt                       | Less than             | `books(filter: { year: { lt: 1990 } })`
lte                      | Less than or equal    | `books(filter: { year: { lte: 1990 } })`
isNull                   |                       | `books(filter: { year: { isNull: true} })`
**String Operators**     |                       |
contains                 | Contains              | `books(filter: { title: { contains: "Foundation" } })`
notContains              | Doesn't Contain       | `books(filter: { title: { notContains: "Foundation" } })`
startsWith               | Starts with           | `books(filter: { title: { startsWith: "Foundation" } })`
endsWith                 | End with              | `books(filter: { title: { endsWith: "Empire" } })`
**Logical Operators**    |                       |
and                      | Logical and           | `authors(filter: { and: [ { first_name: { eq: "Robert" } } { last_name: { eq: "Heinlein" } } ] })`
or                       | Logical or            | `authors(filter: { or: [ { first_name: { eq: "Isaac" } } { first_name: { eq: "Dan" } } ] })`

### `orderBy`

The value of the `orderby` set the order with which the items in the resultset will be returned. For example:

```graphql
{
  books(orderBy: {title: ASC} )
  {
    items {
      id
      title
    }
  }
}
```

will return books ordered by `title`.

### `first` and `after`

The parameter `first` allows to limit the number of items returned. For example:

```graphql
query {
  books(first: 5)
  {
    items {
      id
      title
    }
    hasNextPage
    endCursor
  }
}
```

will return the first 5 books. If no `orderBy` is not specified items will be ordered by the underlying primary key. The value provided to `orderBy` must be a positive integer.

If there are more items in the `book` entity than those requested via `first`, the `hasNextPage` field will evaluate to `true` and the `endCursor` will return a string that can be used with the `after` parameter to access the next items. For example:

```graphql
query {
  books(first: 5, after: "W3siVmFsdWUiOjEwMDQsIkRpcmVjdGlvbiI6MCwiVGFibGVTY2hlbWEiOiIiLCJUYWJsZU5hbWUiOiIiLCJDb2x1bW5OYW1lIjoiaWQifV0=")
  {
    items {
      id
      title
    }
    hasNextPage
    endCursor
  }
}
```

## Mutations

### Create

WIP

### Update

WIP

### Delete

WIP
