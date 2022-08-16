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

## Queries

Each entity support to types of Queries:

- Query by Primary key
- Generic Query

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

WIP

### `orderBy`

WIP

### `first` and `after`

WIP