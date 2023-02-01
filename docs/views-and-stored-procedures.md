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

Entities can represent additional database object types like stored procedures. An entity's source configuration must denote the type `stored-procedure`.

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
  "rest": {
    "methods": [ "POST" ]
  },
  "graphql": {
    "operation": "Mutation"
  },
  "permissions": [{
   "role": "anonymous",
    "actions": [ "read" ]
  }]
}
```

The `parameters` defines which parameters should be exposed and to provide default values to be passed to the stored procedure parameters, if those are not provided in the HTTP request. Additionally, the `rest` and `graphql` sections defines the REST and GraphQL endpoint behavior of the entity, respectively.

**ATTENTION**:

1. Only the first result set returned by the stored procedure will be used by Data API Builder.
2. Stored procedure backed entities only support the action **execute** and runtime initialization will fail when that constraint is not met.

Please note that **you should configure the permission accordingly with the stored procedure behavior**. 

For example, for stored procedures that create new records in a database, consider restricting the allowed REST endpoint `methods` to **POST** and defining the GraphQL `operation` as **mutation.** If a stored procedure limits its functionality to only reading and returning existing records, consider restricting the allowed REST endpoint `methods` to **GET** and defining the GraphQL `operation` as **query.** 

In general, align the REST and GraphQL endpoint configuration with the stored procedure's defined behavior.

### REST support for stored procedures

Entities backed by a stored procedure do not have all the capabilities automatically provided for entities backed by tables, collections or views. A stored procedure backed entity does not support result pagination, ordering, filtering, nor returning records identified by primary key values in the URL.

If the stored procedure accepts parameters, those can be passed in the URL query string when calling the REST endpoint. For example:

```text
http://<dab-server>/api/GetCowrittenBooksByAuthor?author=isaac%20asimov
```

If a parameter is specified both in the configuration file and in the URL query string, the one in the URL query string will take precedence.

### GraphQL support for stored procedures

Just like for REST, entities backed by a stored procedure do not have all the capabilities automatically provided for entities backed by tables, collections or views. A stored procedure backed entity does not support result pagination, ordering, filtering, nor returning records identified by primary key values in the URL.

Depending on the GraphQL `operation` defined in the runtime configuration, a different schema field will be generated. When the `operation` is `query` a query field is created and a mutation field is created when `operation` is `mutation`.

If the stored procedure accepts parameters, the parameters can be passed as an input argument to the query or mutation. For example:

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

If a parameter is specified both in the configuration file and in the URL query string for a stored procedure, the one in the URL query string will take precedence.
