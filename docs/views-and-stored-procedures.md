# Views and Stored Procedures

- [Views](#views)
- [Stored Procedures](#stored-procedures)

## Views

### Configuration

Views can be used similar to how a table can be used in Data API Builder. View usage must be defined by specifying the source type for the entity as `view`. Along with that `key-fields` must be provided, so that Data API Builder knows how it can identify and return a single item, if needed.

If you have a view, for example [`dbo.vw_books_details`](../samples/getting-started/azure-sql-db/library.azure-sql.sql#L112) it can be exposed using the following `dab` command:

```sh
dab add BookDetail --source dbo.vw_books_details --source.type View --source.key-fields "id" --permissions "anonymous:read"
```

the `dab-config.json` file will look like the following:

```json
"BookDetail": {
  "source": {
    "type": "view",
    "object": "dbo.vw_books_details",
    "key-fields": [ "id" ]
  },
  "permissions": [{
    "role": "anonymous",
    "actions": [ "read" ]
  }]
}
```

Please note that **you should configure the permission accordingly with the ability of the view to be updatable or not**. If a view is not updatable, you should only allow a read access to the entity based on that view.

### REST support for views

A view, from a REST perspective, behaves like a table. All REST operations are supported.

### GraphQL support for views

A view, from a GraphQL perspective, behaves like a table. All GraphQL operations are supported.

## Stored procedures

Stored procedures can be used as objects related to entities exposed by Data API Builder. Stored Procedure usage must be defined specifying that the source type for the entity is `stored-procedure`.

If you have a stored procedure, for example [`dbo.stp_get_all_cowritten_books_by_author`](../samples/getting-started/azure-sql-db/library.azure-sql.sql#L138) it can be exposed using the following `dab` command:

```sh
dab add GetCowrittenBooksByAuthor --source dbo.stp_get_all_cowritten_books_by_author --source.type "stored-procedure" source.params "searchType:s" --permissions "anonymous:read" --rest true --graphql true
```

the `dab-config.json` file will look like the following:

```json
"GetCowrittenBooksByAuthor": {
  "source": {
    "type": "stored-procedure",
    "object": "dbo.stp_get_all_cowritten_books_by_author",
    "parameters": {
      "searchType": "s"
    }
  },
  "rest": true,
  "graphql": true
  "permissions": [{
   "role": "anonymous",
    "actions": [ "read" ]
  }]
}
```

The `parameters` defines which parameters should be exposed and to provide default values to be passed to the stored procedure parameters, if those are not provided in the HTTP request.

**ATTENTION**:

1. Only the first result set returned by the stored procedure will be used by Data API Builder.
1. If more than one CRUD action is specified in the config, runtime initialization will fail due to config validation error.

Please note that **you should configure the permission accordingly with the stored procedure behavior**. For example, if a Stored Procedure create a new item in the database, it is recommended to allow only the action `create` for such stored procedure. If, like in the sample, a stored procedure returns some data, it is recommended to allow only the action `read`. In general the recommendation is to align the allowed actions to what the stored procedure does, so to provide a consistent experience to the developer.

### REST support for stored procedures

Entities backed by a stored procedure, do not have all the capabilities automatically provided for entities backed by tables, collections or views. An entity using a stored procedure will not have support for pagination, ordering, filtering or for returning an item by specifying the primary key values.

If the stored procedure accepts parameters, those can be passed in the URL query string when calling the REST endpoint. For example:

```text
http://<dab-server>/api/GetCowrittenBooksByAuthor?author=isaac%20asimov
```

If a parameter is specified both in the configuration file and in the URL query string, the one in the URL query string will take precedence.

### GraphQL support for stored procedures

Just like for REST, entities backed by a stored procedure, do not have all the capabilities automatically provided for entities backed by tables, collections or views. An entity using a stored procedure will not have support for pagination, ordering, filtering or for returning an item by specifying the primary key values.

Depending on the `action` defined in the configuration file a GraphQL query object will be generated - if `read` action has been specified - or a mutation object will be created - if `create`, `update` or `delete` action has been specified.

If the stored procedure accepts parameters, those can be passed as parameter of the query or mutation. For example:

```graphql
query {
  GetCowrittenBooksByAuthor(author:"asimov")
   {
    id
    title
    pages
    year
  }
}
```

If a parameter is specified both in the configuration file and in the URL query string, the one in the URL query string will take precedence.
