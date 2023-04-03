# Views and Stored Procedures

- [Views](#views)
- [Stored Procedures](#stored-procedures)

## Views

### Configuration

Views can be used similar to how a table can be used in Data API builder. View usage must be defined by specifying the source type for the entity as `view`. Along with that the `key-fields` property must be provided, so that Data API builder knows how it can identify and return a single item, if needed.

If you have a view, for example [`dbo.vw_books_details`](../samples/getting-started/azure-sql-db/library.azure-sql.sql#L112) it can be exposed using the following `dab` command:

```shell
dab add BookDetail --source dbo.vw_books_details --source.type View --source.key-fields "id" --permissions "anonymous:read"
```

**NOTE: --source.key-fields is mandatory for views when generating config through the CLI.**

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

Stored procedures can be used as objects related to entities exposed by Data API builder. Stored Procedure usage must be defined specifying that the source type for the entity is `stored-procedure`.

If you have a stored procedure, for example [`dbo.stp_get_all_cowritten_books_by_author`](../samples/getting-started/azure-sql-db/library.azure-sql.sql#L138) it can be exposed using the following `dab` command:

```shell
dab add GetCowrittenBooksByAuthor --source dbo.stp_get_all_cowritten_books_by_author --source.type "stored-procedure" source.params "searchType:s" --permissions "anonymous:execute" --rest.methods "get" --graphql.operation "query"
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
    "methods": [ "GET" ]
  },
  "graphql": {
    "operation": "query"
  },
  "permissions": [{
   "role": "anonymous",
    "actions": [ "execute" ]
  }]
}
```

The `parameters` defines which parameters should be exposed and also provides default values to be passed to the stored procedure parameters, if those are not provided in the HTTP request.

**Limitations**:

1. Only the first result set returned by the stored procedure will be used by Data API builder.
2. Only those stored procedures whose metadata for the first result set can be described by [`sys.dm_exec_describe_first_result_set`](https://learn.microsoft.com/sql/relational-databases/system-dynamic-management-views/sys-dm-exec-describe-first-result-set-transact-sql) are supported. 
3. For both REST and GraphQL endpoints: when a stored procedure parameter is specified both in the configuration file and in the URL query string, the parameter in the URL query string will take precedence.
4. Entities backed by a stored procedure do not have all the capabilities automatically provided for entities backed by tables, collections or views. 
    1. Stored procedure backed entities do not support pagination, ordering, or filtering. Nor do such entities support returning items specified by primary key values.
    2. Field/parameter level authorization rules are not supported.

### REST support for stored procedures

The REST endpoint behavior, for stored procedure backed entity, can be configured to support one or multiple HTTP verbs (GET, POST, PUT, PATCH, DELETE). The REST section of the entity would look like the following:

```json
"rest": {
  "methods": [ "GET", "POST" ]
}
```

Any REST requests for the entity will fail with **HTTP 405 Method Not Allowed** when an HTTP method not listed in the configuration is used. e.g. executing a PUT request will fail with error code 405.
If the `methods` section is excluded from the entity's REST configuration, the default method **POST** will be inferred. To disable the REST endpoint for this entity, configure `"rest": false` and any REST requests on the stored procedure entity will fail with **HTTP 404 Not Found**.

If the stored procedure accepts parameters, the parameters can be passed in the URL query string when calling the REST endpoint with the GET HTTP verb. For example:

URL

```text
GET http://<dab-server>/api/GetCowrittenBooksByAuthor?author=isaac%20asimov
```

Executing stored procedures using other HTTP verbs such as POST, PUT, PATCH, DELETE requires parameters to be passed as JSON in the request body. For example:

URL

```text
POST http://<dab-server>/api/GetCowrittenBooksByAuthor
```

Body

```json
{
  "author": "isaac asimov"
}
```

### GraphQL support for stored procedures

Stored procedure execution in GraphQL can be configured using the `graphql` option of a stored procedure backed entity. Explicitly setting the operation of the entity allows you to represent a stored procedure in the GraphQL schema in a way that aligns with the behavior of the stored procedure.
Not setting any value for the operation will result in the creation of a `mutation` operation.

For example, using the value `query` for the `operation` option results in the stored procedure resolving as a query field in the GraphQL schema

CLI Usage:

```sh
dab add GetCowrittenBooksByAuthor --source dbo.stp_get_all_cowritten_books_by_author --source.type "stored-procedure" --source.params "searchType:s" --permissions "anonymous:execute" --rest.methods "GET" --graphql.operation "query"
```

Runtime Configuration:

```json
"graphql": {
  "operation": "query"
}
```

GraphQL Schema Components: type and query field:

```graphql
type GetCowrittenBooksByAuthor {
  id: Int!
  title: String
}
```

In the schema, both query and mutation operations for stored procedures will have `execute` as a prefix. For the above stored procedure the exact query name field generated would be `executeGetCowrittenBooksByAuthor`. The GraphQL type that will be generated is the following:

```graphql
type Query {
  executeGetCowrittenBooksByAuthor(
    searchType: String = "S"
  ): [GetCowrittenBooksByAuthor!]!
}
```

Alternatively, `operation` can be set to `mutation` so that a mutation field represents the stored procedure in the GraphQL schema. The below `dab update` command can be used to change the `operation`:

```sh
dab update GetCowrittenBooksByAuthor --graphql.operation "mutation"
```

Runtime configuration:

```json
"graphql": {
  "operation": "mutation"
}
```

The GraphQL schema that will be generated is the following:

```graphql
type Mutation {
  executeGetCowrittenBooksByAuthor(
    searchType: String = "S"
  ): [GetCowrittenBooksByAuthor!]!
}
```

If the stored procedure accepts parameters, those can be passed as parameter of the query or mutation. For example:

```graphql
query {
  executeGetCowrittenBooksByAuthor(author:"asimov")
   {
    id
    title
    pages
    year
  }
}
```
